# OAuth

Owns OAuth as a credential type. The library never signs in to anything itself; it stores
credentials *that are* OAuth grants — one per service an application talks to, and one per vault
that signs its requests with a bearer token — refreshes them on read, and re-acquires them
interactively when asked.

Every stored credential is self-describing: the authorization endpoint, token endpoint, client id,
scopes, and redirect port are sealed alongside the tokens. That is what lets one `oauth` provider
serve every service, lets refresh need nothing from the application or the manifest, and lets
`ReauthorizeAsync(namespace, key)` take only an address.

`OAuthTokenProvider` implements the provider contract and `IReauthorizableProvider`. A read takes
an unlocked fast path while the token is fresh past the skew; otherwise it takes the namespace lock,
re-reads inside it, and refreshes only if still stale, so N concurrent expired readers produce one
token-endpoint call. A rejected refresh marks the entry and fails as `Forbidden`; a grant the server
rotated but the publish lost is held in memory and re-published on the next read.

Reads never open a browser. `AuthorizeAsync` and `ReauthorizeAsync` run
`OAuthAuthorizationClientLoopback` — a loopback listener, a PKCE challenge, a `state` the callback
must echo, and a code exchange — and only because the application called them from a context it
chose. The browser is an `IBrowserLauncher` from the container, which is how a test drives the real
listener without one. The token endpoint is reached through one borrowed `HttpClient`, from
`IHttpClientFactory` when the host registered one.

`Domain/` holds the grant and the PKCE challenge; `Infrastructure/` holds the provider, the flow,
the endpoint client, the listener, the shell browser launcher, and the JSON contexts. The feature
has no covering contract of its own: the composer builds the provider directly, and nothing else
in the package reaches into it.
