using WilkenAutomation.Application.Configuration;
using WilkenAutomation.Application.Models;
using WilkenAutomation.Application.Services;
using WilkenAutomation.Infrastructure.Repositories;

namespace WilkenAutomation.Tests;

public class AuthServiceTests : IDisposable
{
    private readonly TestContext _ctx = new();
    private readonly AuthService _auth;

    public AuthServiceTests()
    {
        var jwt = new JwtTokenService(new JwtOptions
        {
            Key = "WilkenAutomation-DevOnly-ChangeThisKey-32ch",
            AccessTokenMinutes = 15,
            RefreshTokenDays = 7
        });
        _auth = new AuthService(new UserRepository(_ctx.Db), new RefreshTokenRepository(_ctx.Db), jwt);
    }

    [Fact]
    public async Task Register_ThenLogin_IssuesShortLivedAccessAndRefresh()
    {
        var registered = await _auth.RegisterAsync(new RegisterRequestDto
        {
            Email = "user@example.com",
            Password = "Secret123",
            DisplayName = "User"
        }, "127.0.0.1", CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(registered.Token));
        Assert.False(string.IsNullOrWhiteSpace(registered.RefreshToken));
        Assert.True(registered.ExpiresAt > DateTime.UtcNow.AddMinutes(5));
        Assert.True(registered.ExpiresAt <= DateTime.UtcNow.AddMinutes(16));
        Assert.True(registered.RefreshExpiresAt > DateTime.UtcNow.AddDays(6));

        var login = await _auth.LoginAsync(new LoginRequestDto
        {
            Email = "user@example.com",
            Password = "Secret123"
        }, "127.0.0.1", CancellationToken.None);

        Assert.Equal(registered.User.Email, login.User.Email);
        Assert.NotEqual(registered.RefreshToken, login.RefreshToken);
    }

    [Fact]
    public async Task Refresh_RotatesToken_AndRejectsReuse()
    {
        var session = await _auth.RegisterAsync(new RegisterRequestDto
        {
            Email = "rotate@example.com",
            Password = "Secret123"
        }, null, CancellationToken.None);

        var rotated = await _auth.RefreshAsync(session.RefreshToken, null, CancellationToken.None);
        Assert.NotEqual(session.RefreshToken, rotated.RefreshToken);
        Assert.NotEqual(session.Token, rotated.Token);

        var reuse = await Assert.ThrowsAsync<AuthException>(() =>
            _auth.RefreshAsync(session.RefreshToken, null, CancellationToken.None));
        Assert.Equal("REFRESH_REUSE", reuse.ErrorCode);

        var afterReuse = await Assert.ThrowsAsync<AuthException>(() =>
            _auth.RefreshAsync(rotated.RefreshToken, null, CancellationToken.None));
        Assert.Equal("REFRESH_REUSE", afterReuse.ErrorCode);
    }

    [Fact]
    public async Task WeakPassword_IsRejected()
    {
        var ex = await Assert.ThrowsAsync<AuthException>(() => _auth.RegisterAsync(new RegisterRequestDto
        {
            Email = "weak@example.com",
            Password = "nodigits"
        }, null, CancellationToken.None));
        Assert.Equal("WEAK_PASSWORD", ex.ErrorCode);
    }

    [Fact]
    public void AccessToken_IsClampedToFifteenMinutesByDefault()
    {
        var jwt = new JwtTokenService(new JwtOptions
        {
            Key = "WilkenAutomation-DevOnly-ChangeThisKey-32ch",
            AccessTokenMinutes = 480
        });
        Assert.Equal(60, jwt.AccessTokenMinutes);
    }

    [Fact]
    public void WorkerToken_UsesSeparateAudienceAndIsClamped()
    {
        var jwt = new JwtTokenService(new JwtOptions
        {
            Key = "WilkenAutomation-DevOnly-ChangeThisKey-32ch",
            WorkerKey = "WilkenAutomation-DevOnly-WorkerKey-32chars!",
            WorkerTokenHours = 24
        });
        Assert.Equal(4, jwt.WorkerTokenHours);
        Assert.Equal("WilkenAutomation.Worker", jwt.WorkerAudience);
        Assert.False(string.IsNullOrWhiteSpace(jwt.CreateWorkerToken()));
    }

    [Fact]
    public async Task Register_WhenDisabled_IsRejected()
    {
        var jwt = new JwtTokenService(new JwtOptions
        {
            Key = "WilkenAutomation-DevOnly-ChangeThisKey-32ch"
        });
        var auth = new AuthService(
            new UserRepository(_ctx.Db),
            new RefreshTokenRepository(_ctx.Db),
            jwt,
            new AuthOptions { AllowRegistration = false });

        var ex = await Assert.ThrowsAsync<AuthException>(() => auth.RegisterAsync(new RegisterRequestDto
        {
            Email = "locked@example.com",
            Password = "Secret123"
        }, null, CancellationToken.None));
        Assert.Equal("REGISTRATION_DISABLED", ex.ErrorCode);
    }

    public void Dispose() => _ctx.Dispose();
}

public class RunCounterTests
{
    [Fact]
    public void FromGroups_ComputesTerminalAndOpen()
    {
        var counts = RunCounters.FromGroups(new[]
        {
            (WilkenAutomation.Application.Enums.JobStatus.Pending, 2),
            (WilkenAutomation.Application.Enums.JobStatus.SuccessWithData, 3),
            (WilkenAutomation.Application.Enums.JobStatus.FailedFinal, 1)
        });
        Assert.Equal(6, counts.Total);
        Assert.Equal(4, counts.Terminal);
        Assert.Equal(2, counts.Open);
        Assert.Equal(0, counts.Running);
    }
}
