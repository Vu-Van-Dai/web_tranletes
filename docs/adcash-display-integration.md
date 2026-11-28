# Adcash Display integration

RongNho integrates Adcash Display as a dedicated provider instead of accepting the provider's JavaScript as arbitrary HTML.

## Supported format

- Adcash **Display** zones only.
- A numeric Zone ID (for example, `12501966`) is stored in `placementId`.
- The site loads the official shared library `https://acscdn.com/script/aclib.js` once per document.
- Each placement calls `aclib.runBanner({ zoneId, renderIn })` with a unique container selector.
- The library and zone call are not copied into the HTML field. Autotag, Pop-Under, Interstitial, In-Page Push and Video Slider are not handled by this Display adapter.

Adcash's publisher documentation says a zone integration consists of the Adcash library and a zone script, that the library need not be included again if already present, and that Display can target a specific element through `renderIn`.

## Admin setup

1. Confirm that Adcash has reviewed and approved the publisher website and the Display zone.
2. Open Admin → Quảng cáo → Thêm quảng cáo.
3. Select provider **Adcash**. The format is locked to **Banner trong trang**.
4. Enter the numeric Display Zone ID from Adcash → Zones → My Zones.
5. Keep the 300×250 dimensions for zone `12501966`, and select the intended placement (the default suggestion is `reader_bottom`).
6. Confirm the website and zone have been approved, then enable the unit.
7. In Provider Settings, enable Adcash and enable the global direct-script switch only after the publisher site and ad category have been reviewed.

The provider switch defaults to enabled when no explicit false value is stored, but the global direct-script switch defaults to off. A unit also needs its own approval flag. The public API filters the unit unless all gates pass.

## Runtime safeguards

- Direct third-party code runs only for anonymous visitors. Authenticated and loading sessions do not receive this unit.
- The Zone ID must match `^[0-9]{1,20}$`. Raw HTML, arbitrary script URLs, image creatives and non-Display Adcash formats are rejected for this provider.
- The shared library is loaded once. The same Zone ID cannot be mounted twice in one document.
- The zone is rendered into a dedicated container; the site does not wrap the Adcash tag in its own sandboxed iframe.
- SPA navigation after a direct network script has run triggers the existing document reload boundary.
- A missing creative can fall through to the existing configured banner fallback. Local diagnostics are not proof of a paid impression.

## Compliance

Adcash's publisher terms require publishers to respect third-party intellectual-property rights and state that advertisement tags may be placed in iframes only with prior written approval. This adapter does not use an outer sandbox iframe. Do not enable the provider until the site and the content displayed on it meet Adcash's publisher requirements and the publisher website has been approved by Adcash.
