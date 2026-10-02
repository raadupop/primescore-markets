# Recommendation rules and disclosures: working memo for the prototype

Analyst working memo, 27 September 2026; not legal advice. Inputs: `doc/briefs/markets-product-study.md` and
`doc/briefs/markets-v2-build-brief.md` Part A (daily regime brief, identical for all, Telegram and email, hash-chained
public record, no execution, paid pilot later). **Uncertain** marks unsettled or unverified points.

## 1. EU rules in plain language

**1.1 MiFID II: personal versus general.** "Investment advice" is a *personal* recommendation, one presented as
suitable for a person or based on that person's circumstances; a recommendation "issued exclusively to the public" is
not personal ([Delegated Regulation 2017/565, Art. 9](https://eur-lex.europa.eu/eli/reg_del/2017/565/oj)). A
newsletter sent to many people is a "general recommendation", an ancillary service (MiFID II Annex I B(5)) for which
authorisation "shall in no case be granted" on its own ([MiFID II Art. 6(1)](https://eur-lex.europa.eu/legal-content/EN/TXT/?uri=CELEX:32014L0065)):
no licence is possible or needed. ESMA's [2023 supervisory briefing](https://www.esma.europa.eu/sites/default/files/2023-07/ESMA35-43-3861_Supervisory_briefing_on_understanding_the_definition_of_advice_under_MiFID_II.pdf)
lists the traps: a message to a group is still personal if targeted or worded "for you" (paras 79, 85); collecting a
risk profile and returning a matching structure is advice (paras 41-43); a disclaimer does not change a
communication's character (para 65); listings, comparisons and software alerts that a level was reached are
information, not advice (para 32).

**1.2 MAR Article 20 and Regulation 2016/958.** An "investment recommendation" is information "recommending or
suggesting an investment strategy, explicitly or implicitly, concerning one or several financial instruments ...
intended for distribution channels or for the public" ([MAR Art. 3(1)(35)](https://eur-lex.europa.eu/eli/reg/2014/596/oj)).
It binds anyone, professional or not ([ESMA statement, 28 Oct 2021](https://www.esma.europa.eu/press-news/esma-news/esma-addresses-investment-recommendations-made-social-media-platforms)).
A standardised, pre-planned note that implicitly suggests a strategy counts "irrespective of its name or label";
purely factual content does not ([ESMA MAR Q&A 8.1, 8.5](https://www.esma.europa.eu/sites/default/files/library/esma70-145-111_qa_on_mar.pdf)).
"Conditions met" plus a straddle template counts. Producers must "take reasonable care" that content is objectively
presented and disclose interests or conflicts (MAR Art. 20(1)). [Regulation 2016/958](https://eur-lex.europa.eu/eli/reg_del/2016/958/oj)
specifies: identity of every responsible person and the legal entity (Art. 2); facts separated from interpretation,
sources indicated, projections labelled, "the date and time when the production of the recommendation was completed"
(Art. 3(1)); for "experts", anyone repeatedly proposing investment decisions with apparent expertise (Art. 1):
methodology summary, meaning and horizon of each rating, update frequency, price timestamp, changes versus earlier
calls, and "a list of all their recommendations ... disseminated during the preceding 12-month period" with date,
author, direction and validity (Art. 4(1); a link suffices, Art. 4(2)); holdings or relationships that could impair
objectivity (Arts 5-6); date and time of first dissemination (Art. 7; Q&A 8.6). The ledger produces all of this if
the record page lists every brief with state, decision, times and hash.

**1.3 Does MAR reach VIX and OVX options traded on Cboe?** MAR covers instruments traded on an EU regulated market,
MTF or OTF, plus instruments "the price or value of which depends on or has an effect on" such instruments
(Art. 2(1)(a)-(d)), for acts "in the Union and in a third country" (Art. 2(4)). A derivative traded outside an EU venue
is in scope only if that price link exists; the producer must assess and document it case by case (Q&A 8.7). Cboe VIX
options trade on no EU venue, but EU-listed products track VIX futures ([Amundi VIX Futures UCITS ETF](https://www.borsaitaliana.it/borsa/etf/scheda/LU0832435464-ETFP.html?lang=en),
[WisdomTree VIX ETPs](https://www.wisdomtree.eu/it-it/etps/equities/wisdomtree-sp-500-vix-short-term-futures-225x-daily-leveraged)).
Whether that link suffices is **uncertain**; no regulator has ruled. Practical answer: comply as if Article 20
applies, since the ledger already generates everything required, and never name an EU-listed ETF or CFD, which would
settle scope against you.

**1.4 Romania (ASF).** ASF is the MAR competent authority ([Law 24/2017 Art. 124](https://asfromania.ro/files/capital/legi/Lege%2024_2017.pdf)).
Breaching Article 20 draws fines of 1,000 to 2,200,000 lei for natural persons and 7,000 to 4,500,000 lei for legal
persons ([Art. 154](https://lege5.ro/Gratuit/ha3tmojxge2q/raspunderi-si-sanctiuni-lege-24-2017?dp=gqydsmzvge2tani)).
The sharper edge is MiFID transposition: [Law 126/2018](https://legislatie.just.ro/Public/DetaliiDocument/201860)
defines investment advice as personal recommendations to a client (Art. 3(1)), and providing investment services
without authorisation is a criminal offence ([Art. 262](https://lege5.ro/Gratuit/gi4dmnzsha4a/sanctiuni-si-masuri-administrative-lege-126-2018?dp=gi3dcobqg42tsna),
via Penal Code Art. 348: 3 months to 1 year or a fine). Sliding from general to personal is criminal exposure, not
paperwork. ASF's 2026-2028 strategy announces monitoring of financial influencers and "specialised discussion groups"
([Economedia](https://economedia.ro/asf-va-monitoriza-influencerii-financiari-site-urile-si-reclamele-online-pentru-a-preveni-fraudele.html)).
No Romania-specific content rule beyond MAR was found (**uncertain**).

## 2. US rules

The Advisers Act catches anyone who, for compensation, advises on securities "either directly or through publications
or writings", but excludes "the publisher of any bona fide newspaper, news magazine or business or financial
publication of general and regular circulation" ([15 U.S.C. 80b-2(a)(11)(D)](https://www.law.cornell.edu/uscode/text/15/80b-2)).
VIX options are securities ([15 U.S.C. 78c(a)(10)](https://www.law.cornell.edu/uscode/text/15/78c)); VIX futures fall
to the CFTC, which excludes publishers "of general and regular dissemination" ([7 U.S.C. 1a(12)(B)(iv)](https://www.law.cornell.edu/uscode/text/7/1a))
and, under [Rule 4.14(a)(9)](https://www.law.cornell.edu/cfr/text/17/4.14), exempts without filing anyone who neither
directs accounts nor gives advice "tailored to ... particular clients".

[Lowe v. SEC, 472 U.S. 181 (1985)](https://www.law.cornell.edu/supremecourt/text/472/181) sets three tests:
impersonal ("the mere fact that a publication contains advice and comment about specific securities does not give it
the personalized character"); bona fide (disinterested analysis, not "hit and run tipsters" or "touts"); regular
("offered to the general public on a regular schedule", not "timed to specific market activity"). The exclusion holds
"as long as the communications ... remain entirely impersonal and do not develop into the kind of fiduciary,
person-to-person relationships" of an adviser.

What breaks it. [Weiss Research (SEC 2006)](https://www.sec.gov/files/litigation/admin/2006/ia-2525.pdf) lost it by
auto-trading subscriber accounts and "personalized communications with its subscribers regarding investment advice":
about USD 2.2m in disgorgement and penalties. In [SEC v. Park ("Tokyo Joe", 2000)](https://www.sec.gov/enforcement-litigation/litigation-releases/lr-16925)
a paid website with member e-mails and a chat room was "sufficiently alleged" to be an adviser; scalping and false
performance claims cost USD 755k. A [2026 practitioner deck](https://www.grsm.com/wp-content/uploads/2026/06/Legal-Compliance-for-Financial-Publishers-and-Trading-Educators-January-2026.pdf)
lists the triggers: personalised sizing, one-to-one trade reviews, segmenting users by financial data, discretion over
timing or execution. The FTC treats trading publishers' performance claims as advertising needing substantiation and
typicality disclosure ([RagingBull.com, USD 2.4m, 2022](https://www.ftc.gov/news-events/news/press-releases/2022/03/online-investment-site-pay-more-24-million-bogus-stock-earnings-claims-hard-cancel-subscription)).
State adviser laws generally mirror the federal exclusion (**uncertain**; not checked).

## 3. Design rules mapped to the prototype

| # | Rule | Why | Feature |
| --- | --- | --- | --- |
| 1 | One brief per market, byte-identical for all; hash recorded before any send | Impersonal (Lowe); "exclusively to the public" (2017/565 Art. 9) | A5, A6 |
| 2 | Fixed daily schedule; the pilot setup alert is a section of that send, never an ad-hoc bulletin | "Regular", not "timed to specific market activity" (Lowe) | A5, A8 |
| 3 | Straddle template shown for one fixed budget (e.g. USD 1,000), same for all, labelled illustrative; no per-member budget field, no stored profile | Sizing to a person is personal advice (ESMA 2023 paras 41-43); 4.14(a)(9) | A8: budget operator-set, never member-set |
| 4 | No account, portfolio or net-worth questions anywhere; email only | Segmenting by financial data is a trigger | A7 |
| 5 | Telegram is one-way; any discussion group has a pinned rule: methodology and data questions only; nobody comments on a member's position, size, entry or exit | Person-to-person contact (Lowe, Weiss, Park); private messages can be personal (ESMA 2023 para 79) | A6, pilot channel |
| 6 | No execution, broker links, auto-trade or broker affiliate fees | Weiss Research | keep out of scope |
| 7 | Disclosure block on every brief and alert; record page lists every brief with time produced, time sent, market, state, decision, hash; founder and engineer declare VIX/OVX positions and log changes | MAR Art. 20; 2016/958 Arts 2-7 | A4, A5, A6, site |
| 8 | Record page shows index moves with sample sizes and the query; never hypothetical P&L, "would have made" or return figures; marketing quotes only the page | FTC substantiation; MAR objectivity | A2, A4, T3 copy |
| 9 | Facts (levels, statistics) visually separated from interpretation (state, reason); FRED attributed; projections labelled | 2016/958 Art. 3(1) | A3, A5 |
| 10 | Name instruments generically ("long-volatility structures such as VIX option straddles"); never name EU-listed ETFs or CFDs | Keeps MAR scope open in your favour (Q&A 8.7) | A5, A8 |
| 11 | Terms of service: publisher of general information, no adviser-client relationship; tick-box at sign-up | Evidence of impersonal relationship | A7 |

## 4. Disclosure texts

**Block for every brief and alert (88 words):**

> PrimeScore Markets is a general information publication sent identically to all subscribers. It is not investment
> advice or a personal recommendation and does not consider your situation. Options can lose the full premium paid;
> past regime statistics do not predict future moves. Produced by [legal entity], author [name], completed [date,
> time UTC], first sent [date, time UTC], hash [hash]. Method, definitions, sources, conflicts of interest and every
> brief of the last 12 months: primescore.ai/markets/method. Index levels as reported by FRED. Decide for yourself or
> consult a licensed adviser.

**Methodology and disclosures page (website):**

*Who produces this.* Legal entity, registered office, the natural persons responsible for model and text; the company
is not an authorised investment firm anywhere and provides no personal recommendations, portfolio management or
execution.

*What a brief is.* One text per market every NYSE trading day after FRED publishes the previous close: regime state
(Compressed, Normal, Stretched, Extreme), severity and confidence, change since the prior close, decision
("Conditions met" or "Wait"), and the historical distribution of index moves after similar states. Each state and
decision is defined; horizons are 1, 5, 10 and 21 trading days.

*Method.* Five-year rolling history, signed severity, confidence, per-market combination, conditions, versioned
configuration; link to the configuration version and the replay command. Outcome statistics (medians, hit rates,
bootstrap intervals) carry sample sizes and are index moves, not returns of any trade.

*Record.* Every brief's completion time, send time, hash and content for at least 12 months, and how to verify a
hash against the ledger.

*Sources.* FRED series VIXCLS and OVXCLS, attribution, publication delay; no real-time data.

*Conflicts of interest.* Positions of the company and named persons in VIX or OVX products, updated on each change;
no payment from any issuer, broker or product provider.

*Risk.* Options can expire worthless; a long straddle loses the entire premium if the index does not move enough; the
template uses a fixed budget and an illustrative premium and is sized for nobody.

## 5. Questions that still need a lawyer, ranked by risk

1. **Romania, criminal edge.** Do the pilot setup alert with a straddle template and a members' channel remain a
   "general recommendation" under Law 126/2018, or does any element make it unauthorised investment advice
   (Art. 262)? The only question with criminal exposure.
2. **MAR scope for US-only instruments.** Confirm the Art. 2(1)(d) analysis, document it, and decide whether to comply
   voluntarily (recommended) or rely on being out of scope.
3. **US exclusion for event-triggered alerts.** Is an alert issued only on "Conditions met", even inside the fixed
   daily send, "timed to specific market activity"? Is a paid Telegram group with any two-way traffic still
   impersonal?
4. **Performance presentation.** Are the record page's hit rates and median moves an earnings or performance claim
   under FTC rules or Romanian consumer law, and what typicality wording do marketing posts need?
5. **Data licence.** Redistributing Cboe index closes obtained via FRED in a paid product may trigger Cboe's own
   licence terms (**uncertain**; a contract question).

Skipping counsel for the free T3 phase is defensible if rules 1-11 are implemented exactly; do not open the paid pilot
(T4) without an answer to question 1.
