// The browser half of a passkey (docs/modules/infrastructure/accounts.md): the server's WebAuthn options in, the
// credential out, both as JSON. The server - ASP.NET Core Identity - does every check; this only asks the phone.
//
// PublicKeyCredential.parseCreationOptionsFromJSON and credential.toJSON() are newer than some iPhones still in use, so
// each has a fallback doing the same base64url <-> bytes conversion by hand.

const fromB64 = s => {
  const b = atob(s.replace(/-/g, '+').replace(/_/g, '/') + '==='.slice((s.length + 3) % 4));
  return Uint8Array.from(b, c => c.charCodeAt(0)).buffer;
};
const toB64 = buf => btoa(String.fromCharCode(...new Uint8Array(buf))).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');

function creationOptions(json) {
  const o = JSON.parse(json);
  if (PublicKeyCredential.parseCreationOptionsFromJSON) return PublicKeyCredential.parseCreationOptionsFromJSON(o);
  return {
    ...o,
    challenge: fromB64(o.challenge),
    user: { ...o.user, id: fromB64(o.user.id) },
    excludeCredentials: (o.excludeCredentials ?? []).map(c => ({ ...c, id: fromB64(c.id) })),
  };
}

function requestOptions(json) {
  const o = JSON.parse(json);
  if (PublicKeyCredential.parseRequestOptionsFromJSON) return PublicKeyCredential.parseRequestOptionsFromJSON(o);
  return { ...o, challenge: fromB64(o.challenge), allowCredentials: (o.allowCredentials ?? []).map(c => ({ ...c, id: fromB64(c.id) })) };
}

function serialise(credential) {
  if (credential.toJSON) return JSON.stringify(credential.toJSON());
  const r = credential.response;
  const response = r.attestationObject
    ? { clientDataJSON: toB64(r.clientDataJSON), attestationObject: toB64(r.attestationObject), transports: r.getTransports?.() ?? [] }
    : { clientDataJSON: toB64(r.clientDataJSON), authenticatorData: toB64(r.authenticatorData), signature: toB64(r.signature),
        userHandle: r.userHandle ? toB64(r.userHandle) : null };
  return JSON.stringify({ id: credential.id, rawId: toB64(credential.rawId), type: credential.type, response,
    authenticatorAttachment: credential.authenticatorAttachment ?? null, clientExtensionResults: credential.getClientExtensionResults?.() ?? {} });
}

/** Whether this browser can make and use passkeys at all. */
export const supported = () => typeof window.PublicKeyCredential === 'function';

/** Makes a passkey from the server's creation options; the credential as JSON, or null when the person cancelled. */
export async function create(optionsJson) {
  try {
    return serialise(await navigator.credentials.create({ publicKey: creationOptions(optionsJson) }));
  } catch (e) {
    if (e?.name === 'NotAllowedError' || e?.name === 'AbortError') return null;
    throw e;
  }
}

// A browser runs one passkey request at a time: the autofill one waits in the background, and is called off before any
// other starts.
let waiting = null;

/** Calls off the autofill request, if one is waiting. */
export function abort() {
  waiting?.abort();
  waiting = null;
}

/**
 * Signs in with a passkey from the server's request options: the browser's own prompt, Face ID or the PIN. The
 * assertion as JSON, or null when cancelled - or refused for want of a tap: Safari only prompts in answer to one.
 */
export async function get(optionsJson) {
  abort();
  try {
    return serialise(await navigator.credentials.get({ publicKey: requestOptions(optionsJson) }));
  } catch (e) {
    if (e?.name === 'NotAllowedError' || e?.name === 'AbortError') return null;
    throw e;
  }
}

/** Whether the browser offers passkeys among a field's autofill suggestions (iPhone: on the keyboard's bar). */
export async function autofillAvailable() {
  try {
    return !!(await window.PublicKeyCredential?.isConditionalMediationAvailable?.());
  } catch {
    return false;
  }
}

/**
 * The autofill sign-in: waits, in the background, until the person picks the passkey from a field marked
 * autocomplete="username webauthn" - then Face ID. The assertion as JSON, or null when called off.
 */
export async function getFromAutofill(optionsJson) {
  abort();
  const mine = waiting = new AbortController();
  try {
    return serialise(await navigator.credentials.get({ publicKey: requestOptions(optionsJson), mediation: 'conditional', signal: mine.signal }));
  } catch (e) {
    if (e?.name === 'NotAllowedError' || e?.name === 'AbortError') return null;
    throw e;
  } finally {
    if (waiting === mine) waiting = null;
  }
}

// That this browser has signed in here before - so it has a passkey to offer, and a sign-in can go straight to it.
// Per device and only a hint: wrong, the prompt simply finds nothing. Never trusted for anything else.
const KNOWN = 'mealplanner-has-passkey';

export function known() {
  try { return localStorage.getItem(KNOWN) === '1'; } catch { return false; }
}

export function remember(has) {
  try { has ? localStorage.setItem(KNOWN, '1') : localStorage.removeItem(KNOWN); } catch { /* private browsing: no hint */ }
}
