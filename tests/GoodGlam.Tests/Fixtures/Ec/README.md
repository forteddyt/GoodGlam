# Recorded Eorzea Collection responses

Real, unedited responses captured from live Eorzea Collection. `EcRecordedResponseTests` replays
them through the actual `EorzeaCollectionClient` and `GlamPopularityService`, so the parsing the
plugin depends on is exercised against markup EC really serves rather than markup we invented.

That distinction is the whole point. Hand-written HTML trivially satisfies our own regexes - it is
shaped by the same assumptions - so it can only prove the parser still does what it did yesterday.
These files can disprove an assumption, because nobody wrote them to fit. `NameRegex`, for instance,
allows at most 200 characters between a card's `href` and its `content-title`; only real markup says
whether that window is actually wide enough.

**Do not hand-edit these files.** Editing them to make a test pass converts a real signal back into a
self-fulfilling one. Re-capture instead.

| File | Request |
|:---|:---|
| `search-body-scion-jacket.json` | `POST /gear/body/search` with `{"search":"Scion Adventurer's Jacket"}` |
| `listing-body-8912.html` | `GET /glamours?filter[orderBy]=loves&filter[bodyPiece]=8912&page=1` |

Captured 2026-07-30. The listing holds 36 glamour cards; the top entry is *Classy Flight Attendant*
with 822 loves.

## What this does not cover

Snapshots are frozen, so they cannot notice **EC changing its own JSON shape or HTML structure** -
exactly what the previous live suite was uniquely good at. If EC restructures its markup, these
tests stay green while the plugin breaks in the wild. Treat a bug report about missing/zero loves as
a prompt to re-capture and diff, since a parser that no longer matches real markup will show up here
immediately once the fixtures are refreshed.

## Refreshing

Capture with the same headers the plugin sends (see `EcRequest`), then verify the result is a real
page and not a Cloudflare interstitial:

```bash
UA='Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36'

curl -s --compressed -H "User-Agent: $UA" \
  -H 'Accept: text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8' \
  'https://ffxiv.eorzeacollection.com/glamours?filter%5BorderBy%5D=loves&filter%5BbodyPiece%5D=8912&page=1' \
  -o listing-body-8912.html

curl -s --compressed -H "User-Agent: $UA" \
  -H 'Accept: application/json, text/plain, */*' -H 'Content-Type: application/json' \
  -H 'X-Requested-With: XMLHttpRequest' \
  -H 'Origin: https://ffxiv.eorzeacollection.com' \
  -H 'Referer: https://ffxiv.eorzeacollection.com/glamours' \
  -X POST 'https://ffxiv.eorzeacollection.com/gear/body/search' \
  --data '{"search":"Scion Adventurer'"'"'s Jacket"}' \
  -o search-body-scion-jacket.json
```

**Always check what you captured.** `curl` exits `0` on a `403`, so a Cloudflare challenge lands in
the file looking like a successful download:

```bash
grep -c 'js-glamour-likes-' listing-body-8912.html   # expect > 0
grep -c 'Just a moment'     listing-body-8912.html   # expect 0
```

EC's Cloudflare edge challenges some egress IPs (datacenter ranges especially, but residential ones
are not exempt), so a capture may need retrying from a different network. If a refresh changes the
loves counts or the top glamour - it will, they move over time - update the expectations in
`EcRecordedResponseTests` to match the new snapshot.
