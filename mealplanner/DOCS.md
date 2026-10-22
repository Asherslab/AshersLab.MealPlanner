# Meal Planner

Plan the household's meals on a calendar, build the shopping list per store, and see what it costs.

## How it is reached

At its own web address - `https://meals.example.com` - through **Nginx Proxy Manager**, with **Authelia** asking who
you are first. Not from the Home Assistant sidebar: an app inside Home Assistant's frame can't be installed on a
phone or opened with no signal, and this one is meant to be both.

The app itself has no login. It believes the name the sign-in proxy gives it, and only from the proxy's address: a
connection from anywhere else is refused. So two things are on you:

- **Never forward this add-on's port on your router.** Only Nginx Proxy Manager should reach it.
- **Nginx Proxy Manager must replace the user header** with Authelia's answer (the set-up below does), never pass on
  whatever the browser sent.

## Set-up

1. **Authelia.** Running anywhere Nginx Proxy Manager can reach (a community add-on, or Docker on another machine),
   with an access-control rule for the meal planner's domain. Two-factor and a long "remember me" are worth having: a
   phone left signed out shows **Signed out** on the sync chip until someone signs in again, and keeps every change on
   the phone until then.
2. **Install and start this add-on.** Its Configuration tab:
   - `trusted_proxies`: where Nginx Proxy Manager connects from. The default, `172.30.32.0/23`, is all of Home
     Assistant's internal network, so any add-on could pretend to be the proxy. Narrow it: in Settings → Add-ons →
     Nginx Proxy Manager, or `ha addons info <its slug>` in the Terminal add-on, find its IP address and put that
     here, written out in full (`172.30.33.4`). If that add-on uses the host's network, it connects from `172.30.32.1`.
   - `user_header`, `display_name_header`: leave as they are for the set-up below.
3. **In Nginx Proxy Manager**, a proxy host for the meal planner's domain: forward to this add-on's **Hostname** (on
   its Info tab, like `abcd1234-mealplanner`), port `8099`, scheme `http`; your SSL certificate with **Force SSL**;
   **Websockets support** off (the app does not use them). On the **Advanced** tab, with the Authelia address in place
   of `authelia:9091` and the add-on's hostname in place of `abcd1234-mealplanner`:

   ```nginx
   location /internal/authelia/authz {
       internal;
       proxy_pass http://authelia:9091/api/authz/auth-request;
       proxy_set_header X-Original-Method $request_method;
       proxy_set_header X-Original-URL $scheme://$http_host$request_uri;
       proxy_set_header X-Forwarded-For $remote_addr;
       proxy_set_header Content-Length "";
       proxy_set_header Connection "";
       proxy_pass_request_body off;
   }

   location / {
       auth_request /internal/authelia/authz;
       auth_request_set $user $upstream_http_remote_user;
       auth_request_set $name $upstream_http_remote_name;
       auth_request_set $redirection_url $upstream_http_location;
       error_page 401 = @signin;

       # Replaces anything the browser sent under these names.
       proxy_set_header X-Forwarded-User $user;
       proxy_set_header X-Forwarded-Name $name;

       # The app holds a change stream open: no buffering, a long read. A stopped add-on answers nothing at all, so give
       # up connecting quickly - the app then says "Offline" at once instead of waiting a minute.
       proxy_connect_timeout 5s;
       proxy_buffering off;
       proxy_read_timeout 1h;
       proxy_http_version 1.1;
       proxy_pass http://abcd1234-mealplanner:8099;
   }

   # Signed out: a page goes to Authelia's login; a sync call (gRPC-Web) gets a plain 401, which the app shows as
   # "Signed out" - a redirect to another domain would look to it like having no signal.
   location @signin {
       if ($http_content_type ~* "grpc-web") { return 401; }
       return 302 $redirection_url;
   }
   ```

4. **Open the address on each phone and install it**: Safari → Share → Add to Home Screen; Chrome → Install app.
   Opened once online, it opens with no signal after that.

## Your data

Everything is stored in this add-on's own data folder, which is included in Home Assistant backups. Each phone or
browser also keeps its own copy so the app keeps working with poor reception, and catches up when it is back online.
