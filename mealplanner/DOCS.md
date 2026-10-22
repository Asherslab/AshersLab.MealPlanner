# Meal Planner

Plan the household's meals on a calendar, build the shopping list per store, and see what it costs.

## How it is reached

At its own web address - `https://meals.example.com` - through **Nginx Proxy Manager**, and installed on each phone
like an app. Not from the Home Assistant sidebar: an app inside Home Assistant's frame can't be installed on a phone or
opened with no signal, and this one is meant to be both.

**You sign in with a passkey** - Face ID, a fingerprint or the phone's PIN; there are no passwords. Every change syncs
only for someone signed in. Signed out, a phone keeps working with what it has and sends its changes once someone
signs in again.

## Set-up

1. **Pick the address** - a name in your domain, say `meals.yourdomain` - and point it at Nginx Proxy Manager, as for
   anything else you expose.
2. **Install this add-on.** On its Configuration tab set `public_url` to that address, `https://meals.yourdomain`, and
   save. **Passkeys are tied to this address for good**: change it later and everyone has to set up again.
3. **Start the add-on and open its Log tab.** It prints a setup link: *"Nobody can sign in yet. Open this link to set up
   a passkey ..."*. It works once, for a week.
4. **In Nginx Proxy Manager**, add a proxy host for the address: forward to this add-on's **Hostname** (on its Info tab,
   like `abcd1234-mealplanner`), port `8099`, scheme `http`; your SSL certificate, with **Force SSL** and **HTTP/2**.
   Nothing on the Advanced tab is needed. Optional, but worth it: on the Advanced tab,

   ```nginx
   # The app holds a sync stream open; a stopped add-on answers nothing, so give up connecting quickly.
   proxy_buffering off;
   proxy_read_timeout 1h;
   proxy_connect_timeout 5s;
   ```

5. **On your phone, open the setup link** from step 3. Write your name and tap **Make my passkey**. You're in.
6. **Invite your household**: in the app, Settings → **Who can sign in** → *Make an invitation link*, and send it to
   them. It works once, for a day; they make their own passkey from it.
7. **Install the app on each phone**: Safari → Share → *Add to Home Screen*; Chrome → *Install app*.

## If something goes wrong

- **A new phone**: passkeys sync through iCloud Keychain or Google Password Manager, so a new phone usually signs in
  with the old one's. If not, someone signed in sends an invitation, or adds a passkey from Settings on a device of theirs.
- **A lost phone**: on another device, Settings → Who can sign in → remove that phone's passkey, or *Sign out on all my
  devices*. Within a minute the lost phone is signed out.
- **Nobody can sign in at all**: turn on `reset_access` on the Configuration tab, restart the add-on, and open the new
  setup link from the Log tab. Turn `reset_access` off again afterwards.

## Your data

Everything is stored in this add-on's own data folder, which is included in Home Assistant backups - the passkeys too,
so a restore keeps everyone able to sign in. Each phone or browser also keeps its own copy so the app keeps working with
poor reception, and catches up when it is back online.
