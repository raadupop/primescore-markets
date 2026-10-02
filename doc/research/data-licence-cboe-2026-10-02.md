# Cboe data licence: what the terms say and what the product does

Working note, 2 October 2026; not legal advice. Sources read that day; quotations are verbatim.

## What Cboe's terms say

- **Website terms, section 2** (<https://www.cboe.com/terms>): "You may view, print and download one
  copy of the Materials for your personal non-commercial use in connection with products and
  services offered by Cboe, provided that you maintain all copyright, trademark and other notices
  contained on the Materials." And: "You may not otherwise copy, reproduce, alter, store either in
  hard copy or in an electronic retrieval system, license, transmit, display, broadcast, create a
  derivative work (for example, a financial product, service or index) from, use to verify or
  correct other data or information, publish, rent, sublicense, distribute, or otherwise use in
  whole or in part in any other manner the Materials without Cboe's prior written consent except to
  the extent that such use constitutes "fair use" under the "Copyright Act of 1976", as amended from
  time to time."
- **Disclaimers** (<https://www.cboe.com/us_disclaimers/>): "No data, values, or other content
  contained in this document (including without limitation, index values or information, ...)
  or any part thereof may be modified, reverse-engineered, reproduced, or distributed in any form or
  by any means, or stored in a database or retrieval system, without the prior written permission
  of Cboe."
- **Use of content** (<https://www.cboe.com/use-of-content>): "In order to use any Cboe logo, data,
  photo/image or other content contained in Cboe websites (collectively 'Cboe Content'), you must
  receive approval in advance from Cboe." Approval "will be contingent upon your execution of a
  license agreement". Cboe "will typically review and respond to requests within five business days".
- **The history-file page** (<https://www.cboe.com/tradable-products/vix/vix-historical-data/>)
  states only that the data "is compiled for the convenience of site visitors and is furnished
  without responsibility for accuracy", and links the terms above.
- The SPX file carries S&P Dow Jones Indices' index; its values are licensed by S&P DJI, separately.

ADR-0009 described the files as free "for internal use with no redistribution". The terms are
narrower: personal non-commercial use only; storing, deriving, publishing and verifying other data
each need written consent.

## What the engine does with the files today

| Use | Where | Within personal non-commercial use? |
| --- | --- | --- |
| Downloads every index file every 15 minutes overnight | Cboe source adapter | Downloading one copy for personal use: yes, if the operator's use stays personal |
| Stores every close in the ledger | ledger, `ing_signals` | "store ... in an electronic retrieval system": needs consent beyond personal use |
| Computes states, percentiles, ratios, priced moves, outcome counts, position scenarios | every page since slice 1 | derivative works: needs consent beyond personal use |
| Compares FRED's closes with Cboe's | FRED cross-check (off by default) | "use to verify or correct other data": needs consent |
| Serves levels and derived figures to READ tokens | API | transmission to others: needs consent |

## What a public brief would publish

Market-derived, all from Cboe files: the VIX and OVX levels, percentiles and states; the
cross-asset percentiles (VIX, VXN, RVX, VVIX, OVX, GVZ); the 9-day/30-day ratio and its percentile
before each event; the event record (priced moves, which reveal VIX9D, actual S&P 500 moves, counts
inside the range and below the straddle estimate, VIX9D changes). Public-domain and publishable
without Cboe: the release calendar (Federal Reserve, BLS, BEA, EIA), canonical ids and reschedules.

## Options

1. **Ask Cboe** through the Request to Use Cboe Content form: name the series (VIX, VIX9D, VIX3M,
   VIX6M, VVIX, OVX, GVZ, VXN, RVX, SPX), the storage, the derived figures above and where they would
   appear. The answer is a licence agreement, likely with fees (unverified). SPX values also need
   S&P DJI.
2. **Buy data under a licence that allows storage and derived output**: Cboe's own data products or a
   vendor whose terms cover index values (fees and terms unverified; the design priced ThetaData's
   value tier at about $40 a month for option data, TODO-013).
3. **Compute the measures from licensed option prices** instead of Cboe's indices, once an option
   feed is bought: the engine would then derive its own implied volatilities. The "VIX" name stays
   Cboe's trademark.
4. **Stay personal research** until one of the above: no sale, no publication, no READ tokens for
   anyone but the operator.

Recommendation: option 4 now (ADR-0015), and option 1 before any customer test, pilot or
public brief, because every customer-facing figure rests on these files.
