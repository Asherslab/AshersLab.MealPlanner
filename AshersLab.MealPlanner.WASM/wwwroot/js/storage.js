// Asks the browser to keep this origin's storage (the OPFS database) instead of evicting it under
// pressure or after weeks unused. Returns null where the API does not exist.
export async function persist() {
    if (!navigator.storage?.persist) return null;
    if (await navigator.storage.persisted()) return true;
    return await navigator.storage.persist();
}
