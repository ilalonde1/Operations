# ScoringDiff — 2026-10-09T17:17:04.6578698+00:00

Compares scores, tiers, ranks and factors for the frozen corpus. Does not validate missing/incorrect source descriptions, rejected/absent rows, UI order, or Deltek ODBC facts. A multi-unit project with no form in its latest title/description can be ranked wrongly in both arms without this diff catching it.

Rows: 8841; descriptions absent: 672; baseline vs stored score/tier drift: 681.
Score increases: 0; decreases: 0. Tier promotions: 0; demotions: 0.

Source groups count an opportunity once per source; cross-posts can appear in multiple source groups. Kind groups partition IDs. Ranks are over the complete corpus, descending score then ID.

| Group | Rows | Before avg | After avg | Before max | After max | Before mean rank | After mean rank | Score up/down | Tier up/down | Missing description |
|---|---:|---:|---:|---:|---:|---:|---:|---|---|---:|
| Kind: Application | 5795 | 12.38 | 12.38 | 31.00 | 31.00 | 4133.1 | 4133.1 | 0/0 | 0/0 | 1 |
| Kind: Award | 80 | 18.41 | 18.41 | 32.00 | 32.00 | 385.7 | 385.7 | 0/0 | 0/0 | 0 |
| Kind: Bid | 2790 | 8.37 | 8.37 | 40.00 | 40.00 | 5044.5 | 5044.5 | 0/0 | 0/0 | 671 |
| Kind: Other | 176 | 7.08 | 7.08 | 20.00 | 20.00 | 5851.4 | 5851.4 | 0/0 | 0/0 | 0 |
| Source: APC_AllBuyers | 389 | 6.65 | 6.65 | 19.00 | 19.00 | 6732.7 | 6732.7 | 0/0 | 0/0 | 382 |
| Source: BcBid | 1339 | 11.91 | 11.91 | 40.00 | 40.00 | 3477.3 | 3477.3 | 0/0 | 0/0 | 49 |
| Source: BcBid_Engineering | 322 | 9.56 | 9.56 | 40.00 | 40.00 | 3713.4 | 3713.4 | 0/0 | 0/0 | 12 |
| Source: BCGovNewsRss | 77 | 3.77 | 3.77 | 20.00 | 20.00 | 7005.2 | 7005.2 | 0/0 | 0/0 | 0 |
| Source: BdAlertsMailbox | 99 | 9.66 | 9.66 | 12.00 | 12.00 | 4954.0 | 4954.0 | 0/0 | 0/0 | 0 |
| Source: BidsTenders_Abbotsford | 15 | -0.87 | -0.87 | 8.00 | 8.00 | 7496.7 | 7496.7 | 0/0 | 0/0 | 15 |
| Source: BidsTenders_Burnaby | 17 | 15.18 | 15.18 | 18.00 | 18.00 | 1844.1 | 1844.1 | 0/0 | 0/0 | 16 |
| Source: BidsTenders_Campbellriver | 2 | 0.00 | 0.00 | 0.00 | 0.00 | 8564.0 | 8564.0 | 0/0 | 0/0 | 2 |
| Source: BidsTenders_Camrose | 1 | 0.00 | 0.00 | 0.00 | 0.00 | 8499.0 | 8499.0 | 0/0 | 0/0 | 1 |
| Source: BidsTenders_Cityofgp | 12 | 0.42 | 0.42 | 4.00 | 4.00 | 8445.8 | 8445.8 | 0/0 | 0/0 | 12 |
| Source: BidsTenders_Comoxvalleyrd | 1 | 0.00 | 0.00 | 0.00 | 0.00 | 8436.0 | 8436.0 | 0/0 | 0/0 | 1 |
| Source: BidsTenders_Coquitlam | 4 | 16.00 | 16.00 | 18.00 | 18.00 | 1500.0 | 1500.0 | 0/0 | 0/0 | 2 |
| Source: BidsTenders_CountyGP | 5 | 3.20 | 3.20 | 6.00 | 6.00 | 7868.6 | 7868.6 | 0/0 | 0/0 | 5 |
| Source: BidsTenders_DNV | 9 | 23.56 | 23.56 | 28.00 | 28.00 | 139.9 | 139.9 | 0/0 | 0/0 | 8 |
| Source: BidsTenders_EPCOR | 6 | 0.17 | 0.17 | 6.00 | 6.00 | 8238.0 | 8238.0 | 0/0 | 0/0 | 6 |
| Source: BidsTenders_Leduc | 8 | 4.00 | 4.00 | 7.00 | 7.00 | 7579.8 | 7579.8 | 0/0 | 0/0 | 8 |
| Source: BidsTenders_Lethbridge | 10 | 1.90 | 1.90 | 6.00 | 6.00 | 8017.2 | 8017.2 | 0/0 | 0/0 | 10 |
| Source: BidsTenders_MapleRidge | 13 | 14.31 | 14.31 | 18.00 | 18.00 | 2201.2 | 2201.2 | 0/0 | 0/0 | 12 |
| Source: BidsTenders_MetroVancouver | 32 | 13.19 | 13.19 | 28.00 | 28.00 | 2935.0 | 2935.0 | 0/0 | 0/0 | 23 |
| Source: BidsTenders_Nanaimo | 7 | 10.29 | 10.29 | 16.00 | 16.00 | 3988.9 | 3988.9 | 0/0 | 0/0 | 7 |
| Source: BidsTenders_Okotoks | 4 | 1.50 | 1.50 | 6.00 | 6.00 | 8031.0 | 8031.0 | 0/0 | 0/0 | 4 |
| Source: BidsTenders_PortMoody | 4 | 12.25 | 12.25 | 18.00 | 18.00 | 3380.2 | 3380.2 | 0/0 | 0/0 | 4 |
| Source: BidsTenders_PrinceGeorge | 5 | 3.80 | 3.80 | 8.00 | 8.00 | 6759.8 | 6759.8 | 0/0 | 0/0 | 5 |
| Source: BidsTenders_Rdco | 2 | 10.00 | 10.00 | 14.00 | 14.00 | 5530.0 | 5530.0 | 0/0 | 0/0 | 2 |
| Source: BidsTenders_RedDeer | 11 | -1.36 | -1.36 | 7.00 | 7.00 | 8240.6 | 8240.6 | 0/0 | 0/0 | 11 |
| Source: BidsTenders_Richmond | 8 | 20.50 | 20.50 | 35.00 | 35.00 | 1202.2 | 1202.2 | 0/0 | 0/0 | 6 |
| Source: BidsTenders_Squamish | 3 | 8.00 | 8.00 | 8.00 | 8.00 | 4691.7 | 4691.7 | 0/0 | 0/0 | 3 |
| Source: BidsTenders_StAlbert | 12 | 2.08 | 2.08 | 6.00 | 6.00 | 7991.7 | 7991.7 | 0/0 | 0/0 | 12 |
| Source: BidsTenders_TOL | 9 | 13.89 | 13.89 | 18.00 | 18.00 | 2511.1 | 2511.1 | 0/0 | 0/0 | 8 |
| Source: BidsTenders_WoodBuffalo | 13 | -0.38 | -0.38 | 0.00 | 0.00 | 8543.6 | 8543.6 | 0/0 | 0/0 | 13 |
| Source: Bonfire_AHS | 9 | 7.11 | 7.11 | 16.00 | 16.00 | 6716.6 | 6716.6 | 0/0 | 0/0 | 0 |
| Source: Bonfire_AthabascaU | 4 | 0.00 | 0.00 | 0.00 | 0.00 | 8507.0 | 8507.0 | 0/0 | 0/0 | 0 |
| Source: Bonfire_BCIT | 10 | 8.00 | 8.00 | 8.00 | 8.00 | 5145.6 | 5145.6 | 0/0 | 0/0 | 0 |
| Source: Bonfire_BCLC | 1 | 8.00 | 8.00 | 8.00 | 8.00 | 4934.0 | 4934.0 | 0/0 | 0/0 | 0 |
| Source: Bonfire_BCTransit | 4 | 8.00 | 8.00 | 8.00 | 8.00 | 4560.5 | 4560.5 | 0/0 | 0/0 | 0 |
| Source: Bonfire_CbeAb | 29 | 3.41 | 3.41 | 6.00 | 6.00 | 7338.9 | 7338.9 | 0/0 | 0/0 | 1 |
| Source: Bonfire_CVRD | 5 | 8.00 | 8.00 | 8.00 | 8.00 | 5120.0 | 5120.0 | 0/0 | 0/0 | 0 |
| Source: Bonfire_Epsb | 14 | 10.43 | 10.43 | 16.00 | 16.00 | 4100.6 | 4100.6 | 0/0 | 0/0 | 1 |
| Source: Bonfire_FNHA | 8 | 2.00 | 2.00 | 8.00 | 8.00 | 7886.5 | 7886.5 | 0/0 | 0/0 | 0 |
| Source: Bonfire_FraserHealth | 62 | 3.55 | 3.55 | 20.00 | 20.00 | 7034.1 | 7034.1 | 0/0 | 0/0 | 0 |
| Source: Bonfire_ICBC | 2 | 8.00 | 8.00 | 8.00 | 8.00 | 4577.0 | 4577.0 | 0/0 | 0/0 | 0 |
| Source: Bonfire_IslandHealth | 4 | 6.50 | 6.50 | 18.00 | 18.00 | 5531.5 | 5531.5 | 0/0 | 0/0 | 0 |
| Source: Bonfire_Kamloops | 5 | 16.40 | 16.40 | 28.00 | 28.00 | 3730.0 | 3730.0 | 0/0 | 0/0 | 0 |
| Source: Bonfire_Kelowna | 21 | 6.29 | 6.29 | 12.00 | 12.00 | 6985.8 | 6985.8 | 0/0 | 0/0 | 0 |
| Source: Bonfire_KPU | 7 | 1.43 | 1.43 | 10.00 | 10.00 | 7960.0 | 7960.0 | 0/0 | 0/0 | 0 |
| Source: Bonfire_MacEwanU | 12 | 0.50 | 0.50 | 6.00 | 6.00 | 8431.9 | 8431.9 | 0/0 | 0/0 | 1 |
| Source: Bonfire_Mission | 7 | 1.57 | 1.57 | 8.00 | 8.00 | 8075.3 | 8075.3 | 0/0 | 0/0 | 0 |
| Source: Bonfire_MountRoyalU | 4 | 0.00 | 0.00 | 0.00 | 0.00 | 8599.5 | 8599.5 | 0/0 | 0/0 | 0 |
| Source: Bonfire_NAIT | 13 | 6.00 | 6.00 | 6.00 | 6.00 | 7061.8 | 7061.8 | 0/0 | 0/0 | 0 |
| Source: Bonfire_Nelson | 1 | 0.00 | 0.00 | 0.00 | 0.00 | 8555.0 | 8555.0 | 0/0 | 0/0 | 0 |
| Source: Bonfire_NorthCowichan | 7 | 0.00 | 0.00 | 0.00 | 0.00 | 8591.4 | 8591.4 | 0/0 | 0/0 | 0 |
| Source: Bonfire_Penticton | 8 | 6.00 | 6.00 | 6.00 | 6.00 | 7110.8 | 7110.8 | 0/0 | 0/0 | 0 |
| Source: Bonfire_PHSA | 11 | 2.18 | 2.18 | 16.00 | 16.00 | 7625.8 | 7625.8 | 0/0 | 0/0 | 0 |
| Source: Bonfire_PrrdBc | 5 | 0.00 | 0.00 | 0.00 | 0.00 | 8506.2 | 8506.2 | 0/0 | 0/0 | 0 |
| Source: Bonfire_Saanich | 4 | 8.00 | 8.00 | 8.00 | 8.00 | 4677.2 | 4677.2 | 0/0 | 0/0 | 0 |
| Source: Bonfire_SAIT | 5 | 6.00 | 6.00 | 6.00 | 6.00 | 7206.2 | 7206.2 | 0/0 | 0/0 | 0 |
| Source: Bonfire_StrathconaCty | 14 | 0.00 | 0.00 | 0.00 | 0.00 | 8595.8 | 8595.8 | 0/0 | 0/0 | 0 |
| Source: Bonfire_TravelAlberta | 3 | 6.00 | 6.00 | 6.00 | 6.00 | 6989.0 | 6989.0 | 0/0 | 0/0 | 0 |
| Source: Bonfire_UAlberta | 26 | 6.00 | 6.00 | 6.00 | 6.00 | 7103.2 | 7103.2 | 0/0 | 0/0 | 4 |
| Source: Bonfire_UBC | 39 | 10.13 | 10.13 | 16.00 | 16.00 | 4313.8 | 4313.8 | 0/0 | 0/0 | 0 |
| Source: Bonfire_UFV | 5 | 0.00 | 0.00 | 0.00 | 0.00 | 8702.8 | 8702.8 | 0/0 | 0/0 | 0 |
| Source: Bonfire_UVic | 26 | 10.27 | 10.27 | 16.00 | 16.00 | 4193.2 | 4193.2 | 0/0 | 0/0 | 0 |
| Source: Bonfire_VCH | 1 | 10.00 | 10.00 | 10.00 | 10.00 | 4310.0 | 4310.0 | 0/0 | 0/0 | 0 |
| Source: Bonfire_Victoria | 23 | 8.17 | 8.17 | 12.00 | 12.00 | 5113.6 | 5113.6 | 0/0 | 0/0 | 0 |
| Source: Calgary_LandUseRedesignation | 864 | 3.01 | 3.01 | 9.00 | 9.00 | 7860.7 | 7860.7 | 0/0 | 0/0 | 0 |
| Source: CampbellRiver_DevelopmentApplications | 39 | 8.00 | 8.00 | 8.00 | 8.00 | 6146.6 | 6146.6 | 0/0 | 0/0 | 0 |
| Source: CanadaBuys | 48 | 1.85 | 1.85 | 22.00 | 22.00 | 7552.3 | 7552.3 | 0/0 | 0/0 | 0 |
| Source: CanadaBuysNew | 37 | 3.00 | 3.00 | 22.00 | 22.00 | 7198.1 | 7198.1 | 0/0 | 0/0 | 0 |
| Source: CivicInfoBC_All | 96 | 2.72 | 2.72 | 12.00 | 12.00 | 7452.7 | 7452.7 | 0/0 | 0/0 | 0 |
| Source: CivicInfoBC_Construction | 5 | 5.60 | 5.60 | 10.00 | 10.00 | 5875.0 | 5875.0 | 0/0 | 0/0 | 0 |
| Source: Colwood_DevelopmentApplications | 30 | 8.00 | 8.00 | 8.00 | 8.00 | 5726.2 | 5726.2 | 0/0 | 0/0 | 0 |
| Source: Comox_BuildingPermits | 35 | 8.00 | 8.00 | 8.00 | 8.00 | 6146.7 | 6146.7 | 0/0 | 0/0 | 0 |
| Source: Comox_PlanningPermits | 12 | 8.00 | 8.00 | 8.00 | 8.00 | 6117.5 | 6117.5 | 0/0 | 0/0 | 0 |
| Source: Coquitlam_DevelopmentApplications | 404 | 18.17 | 18.17 | 28.00 | 28.00 | 842.5 | 842.5 | 0/0 | 0/0 | 0 |
| Source: Courtenay_DevelopmentApplications | 421 | 8.10 | 8.10 | 16.00 | 16.00 | 6288.1 | 6288.1 | 0/0 | 0/0 | 0 |
| Source: CoV_AwardedContracts | 80 | 18.41 | 18.41 | 32.00 | 32.00 | 385.7 | 385.7 | 0/0 | 0/0 | 0 |
| Source: Esquimalt_DevelopmentApplications | 29 | 8.34 | 8.34 | 13.00 | 13.00 | 6694.3 | 6694.3 | 0/0 | 0/0 | 0 |
| Source: Kamloops_PlanningApplications | 46 | 14.35 | 14.35 | 22.00 | 22.00 | 3850.8 | 3850.8 | 0/0 | 0/0 | 0 |
| Source: LACity_RAMP_OpenBids | 110 | 5.35 | 5.35 | 14.00 | 14.00 | 6298.3 | 6298.3 | 0/0 | 0/0 | 0 |
| Source: Langford_DevelopmentApplications | 437 | 8.37 | 8.37 | 18.00 | 18.00 | 5879.8 | 5879.8 | 0/0 | 0/0 | 0 |
| Source: MapleRidge_DevelopmentApplications | 122 | 18.00 | 18.00 | 18.00 | 18.00 | 917.6 | 917.6 | 0/0 | 0/0 | 0 |
| Source: MERX-DCC | 96 | 0.35 | 0.35 | 10.00 | 10.00 | 8310.4 | 8310.4 | 0/0 | 0/0 | 96 |
| Source: Nanaimo_WhatsBuilding | 964 | 16.08 | 16.08 | 26.00 | 26.00 | 2835.4 | 2835.4 | 0/0 | 0/0 | 0 |
| Source: Parksville_DevelopmentApplications | 53 | 8.00 | 8.00 | 8.00 | 8.00 | 6663.0 | 6663.0 | 0/0 | 0/0 | 0 |
| Source: PortMoody_DevelopmentApplications | 17 | 18.71 | 18.71 | 24.00 | 24.00 | 1884.2 | 1884.2 | 0/0 | 0/0 | 0 |
| Source: QualicumBeach_DevelopmentApplications | 113 | 8.00 | 8.00 | 8.00 | 8.00 | 6582.6 | 6582.6 | 0/0 | 0/0 | 0 |
| Source: RDN_DevelopmentApplications | 10 | 16.00 | 16.00 | 16.00 | 16.00 | 3671.5 | 3671.5 | 0/0 | 0/0 | 0 |
| Source: Saanich_DevelopmentApplications | 1059 | 12.08 | 12.08 | 26.00 | 26.00 | 4437.9 | 4437.9 | 0/0 | 0/0 | 1 |
| Source: SamGov | 19 | 4.32 | 4.32 | 12.00 | 12.00 | 6653.9 | 6653.9 | 0/0 | 0/0 | 19 |
| Source: Surrey_DevelopmentApplications | 910 | 18.32 | 18.32 | 31.00 | 31.00 | 1520.1 | 1520.1 | 0/0 | 0/0 | 0 |
| Source: Vancouver_DevelopmentApplications | 100 | 18.09 | 18.09 | 26.00 | 26.00 | 1283.9 | 1283.9 | 0/0 | 0/0 | 0 |
| Source: Victoria_DevelopmentApplications | 130 | 16.57 | 16.57 | 26.00 | 26.00 | 2375.3 | 2375.3 | 0/0 | 0/0 | 0 |

Existing Island view, IsBigTicket=0: 2024; score increases 0, decreases 0. This cohort is NOT a new single-family classifier; it includes TI/accessory/alteration rows and the view's documented ambiguous cases.
