// Tab-scoped recovery evidence only. Server posting operations remain the financial authority.
const key = scope => `stalltrack:direct-fine:v1:${scope}`;
export function read(scope) { return sessionStorage.getItem(key(scope)); }
export function save(scope, intent) { sessionStorage.setItem(key(scope), intent); }
export function clear(scope) { sessionStorage.removeItem(key(scope)); }
