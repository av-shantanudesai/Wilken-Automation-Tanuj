/**
 * Central registry of every localStorage/sessionStorage key the app uses.
 * The string values are part of the persisted contract - never change them.
 */

/** localStorage: 'mock' | 'backend' API mode toggle. */
export const API_MODE_KEY = 'wilken-api-mode';

/** localStorage: id of the run currently selected on the dashboard. */
export const SELECTED_RUN_KEY = 'wilken-selected-run';

/** sessionStorage: serialized auth session (mock mode only). */
export const AUTH_SESSION_KEY = 'wilken-auth';

/** localStorage: registered mock-mode user accounts. */
export const MOCK_USERS_KEY = 'wilken-mock-users';

/** localStorage: persisted state of the in-browser mock export engine. */
export const MOCK_STATE_KEY = 'wilken-mock-state-v1';
