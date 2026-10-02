# Acceptance Cboe fixtures

`VIX_History.csv` and `VIX9D_History.csv` use the real column layout of Cboe's daily index history files (`DATE,OPEN,HIGH,LOW,CLOSE`, dates `MM/dd/yyyy`, six decimals) with **invented values**. No Cboe data is in this repository (Cboe terms: internal use, no redistribution). `SourceAdapterTests` serves them from a local stub server.
