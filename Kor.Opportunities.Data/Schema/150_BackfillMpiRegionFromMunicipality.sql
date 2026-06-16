USE [KorOpportunitiesDb];
GO

/* =====================================================================
   150 — Backfill MajorProjectsInventory.RegionName from MunicipalityName.
   ---------------------------------------------------------------------
   ~2,150 BC/AB rows had a populated MunicipalityName but a NULL (unspecified)
   RegionName, so they were invisible to every regional rollup (the Lower
   Mainland report under-counted Metro Van for exactly this reason). This maps
   municipality -> the canonical region taxonomy from migration 141, EXTENDED
   with 'Northern Alberta' (141's AB set had no bucket for Wood Buffalo / Grande
   Prairie / Cold Lake / Peace River, etc.).

   Matching is by LIKE so address-form values ("Marine Dr, North Vancouver",
   "37th & Heather, Vancouver") backfill too. Within each region the member
   cities never collide across regions (North Vancouver vs Vancouver are both
   Lower Mainland), so substring matches stay correct. Only confident,
   well-known municipalities are mapped; ambiguous tails are deliberately left
   NULL rather than guessed:
     - Cariboo (Williams Lake, Quesnel, 100 Mile House) — Interior/North ambiguous
     - Bow Valley (Canmore, Banff, Kananaskis) — Calgary-region vs own region
     - multi-location / province-wide / bare-address blobs
   US cities mis-tagged Province='BC' (Seattle, San Diego, LA, Portland, ...) are
   NOT mapped here (they correctly stay out of BC regions); correcting their
   Province needs a US-format decision and is a separate follow-up.

   Idempotent: only touches rows where RegionName IS currently NULL/empty.
   ===================================================================== */

/* ---- British Columbia ---------------------------------------------- */
UPDATE opportunities.MajorProjectsInventory
SET RegionName =
  CASE
    /* Vancouver Island (checked before Lower Mainland; no shared substrings) */
    WHEN MunicipalityName LIKE N'%Victoria%' OR MunicipalityName LIKE N'%Saanich%'
      OR MunicipalityName LIKE N'%Esquimalt%' OR MunicipalityName LIKE N'%Oak Bay%'
      OR MunicipalityName LIKE N'%Colwood%' OR MunicipalityName LIKE N'%Langford%'
      OR MunicipalityName LIKE N'%View Royal%' OR MunicipalityName LIKE N'%Sooke%'
      OR MunicipalityName LIKE N'%Sidney%' OR MunicipalityName LIKE N'%Metchosin%'
      OR MunicipalityName LIKE N'%Nanaimo%' OR MunicipalityName LIKE N'%Lantzville%'
      OR MunicipalityName LIKE N'%Ladysmith%' OR MunicipalityName LIKE N'%Duncan%'
      OR MunicipalityName LIKE N'%Cowichan%' OR MunicipalityName LIKE N'%Courtenay%'
      OR MunicipalityName LIKE N'%Comox%' OR MunicipalityName LIKE N'%Cumberland%'
      OR MunicipalityName LIKE N'%Campbell River%' OR MunicipalityName LIKE N'%Port Alberni%'
      OR MunicipalityName LIKE N'%Parksville%' OR MunicipalityName LIKE N'%Qualicum%'
      OR MunicipalityName LIKE N'%Port Hardy%' OR MunicipalityName LIKE N'%Port McNeill%'
      OR MunicipalityName LIKE N'%Alert Bay%' OR MunicipalityName LIKE N'%Tofino%'
      OR MunicipalityName LIKE N'%Ucluelet%' OR MunicipalityName LIKE N'%Mill Bay%'
      OR MunicipalityName LIKE N'%Port Renfrew%' OR MunicipalityName LIKE N'%Departure Bay%'
      OR MunicipalityName LIKE N'%VIU%'
        THEN N'Vancouver Island'
    /* Northern BC */
    WHEN MunicipalityName LIKE N'%Prince George%' OR MunicipalityName LIKE N'%Prince Rupert%'
      OR MunicipalityName LIKE N'%Terrace%' OR MunicipalityName LIKE N'%Kitimat%'
      OR MunicipalityName LIKE N'%Kitamaat%' OR MunicipalityName LIKE N'%Smithers%'
      OR MunicipalityName LIKE N'%Telkwa%' OR MunicipalityName LIKE N'%Houston%'
      OR MunicipalityName LIKE N'%Burns Lake%' OR MunicipalityName LIKE N'%Fraser Lake%'
      OR MunicipalityName LIKE N'%Vanderhoof%' OR MunicipalityName LIKE N'%Fort St. James%'
      OR MunicipalityName LIKE N'%Fort St. John%' OR MunicipalityName LIKE N'%Dawson Creek%'
      OR MunicipalityName LIKE N'%Chetwynd%' OR MunicipalityName LIKE N'%Tumbler Ridge%'
      OR MunicipalityName LIKE N'%Hudson Hope%' OR MunicipalityName LIKE N'%Pouce Coupe%'
      OR MunicipalityName LIKE N'%Taylor%' OR MunicipalityName LIKE N'%Wonowon%'
      OR MunicipalityName LIKE N'%Fort Nelson%' OR MunicipalityName LIKE N'%Mackenzie%'
      OR MunicipalityName LIKE N'%McBride%' OR MunicipalityName LIKE N'%Valemount%'
      OR MunicipalityName LIKE N'%Stewart%' OR MunicipalityName LIKE N'%Kitsault%'
      OR MunicipalityName LIKE N'%Kitwanga%' OR MunicipalityName LIKE N'%Hazelton%'
      OR MunicipalityName LIKE N'%Kispiox%' OR MunicipalityName LIKE N'%Granisle%'
      OR MunicipalityName LIKE N'%Masset%' OR MunicipalityName LIKE N'%Haida Gwaii%'
      OR MunicipalityName LIKE N'%Queen Charlotte%' OR MunicipalityName LIKE N'%Skidegate%'
      OR MunicipalityName LIKE N'%Graham Island%' OR MunicipalityName LIKE N'%Port Edward%'
      OR MunicipalityName LIKE N'%Iskut%' OR MunicipalityName LIKE N'%Dease Lake%'
      OR MunicipalityName LIKE N'%Cassiar%' OR MunicipalityName LIKE N'%Atlin%'
      OR MunicipalityName LIKE N'%Bella Bella%' OR MunicipalityName LIKE N'%Bella Coola%'
      OR MunicipalityName LIKE N'%Alice Arm%' OR MunicipalityName LIKE N'%Nass%'
      OR MunicipalityName LIKE N'%Lelu Island%' OR MunicipalityName LIKE N'%Skeena%'
      OR MunicipalityName LIKE N'%Gitanyow%' OR MunicipalityName LIKE N'%Watson Island%'
      OR MunicipalityName LIKE N'%North Peace%' OR MunicipalityName LIKE N'%West Moberly%'
        THEN N'Northern BC'
    /* Okanagan / Interior */
    WHEN MunicipalityName LIKE N'%Kelowna%' OR MunicipalityName LIKE N'%Vernon%'
      OR MunicipalityName LIKE N'%Penticton%' OR MunicipalityName LIKE N'%Lake Country%'
      OR MunicipalityName LIKE N'%Summerland%' OR MunicipalityName LIKE N'%Osoyoos%'
      OR MunicipalityName LIKE N'%Oliver%' OR MunicipalityName LIKE N'%Peachland%'
      OR MunicipalityName LIKE N'%Kamloops%' OR MunicipalityName LIKE N'%Salmon Arm%'
      OR MunicipalityName LIKE N'%Sicamous%' OR MunicipalityName LIKE N'%Revelstoke%'
      OR MunicipalityName LIKE N'%Cranbrook%' OR MunicipalityName LIKE N'%Nelson%'
      OR MunicipalityName LIKE N'%Castlegar%' OR MunicipalityName LIKE N'%Trail%'
      OR MunicipalityName LIKE N'%Rossland%' OR MunicipalityName LIKE N'%Fernie%'
      OR MunicipalityName LIKE N'%Kimberley%' OR MunicipalityName LIKE N'%Invermere%'
      OR MunicipalityName LIKE N'%Golden%' OR MunicipalityName LIKE N'%Merritt%'
      OR MunicipalityName LIKE N'%Princeton%' OR MunicipalityName LIKE N'%Grand Forks%'
      OR MunicipalityName LIKE N'%Creston%' OR MunicipalityName LIKE N'%Sparwood%'
      OR MunicipalityName LIKE N'%Logan Lake%' OR MunicipalityName LIKE N'%Chase%'
      OR MunicipalityName LIKE N'%Enderby%' OR MunicipalityName LIKE N'%Armstrong%'
      OR MunicipalityName LIKE N'%Lumby%' OR MunicipalityName LIKE N'%Nicola%'
      OR MunicipalityName LIKE N'%Skaha%'
        THEN N'Okanagan/Interior'
    /* Lower Mainland (Metro Van + Fraser Valley + Sea-to-Sky + Sunshine Coast) */
    WHEN MunicipalityName LIKE N'%Vancouver%' OR MunicipalityName LIKE N'%Burnaby%'
      OR MunicipalityName LIKE N'%Surrey%' OR MunicipalityName LIKE N'%Richmond%'
      OR MunicipalityName LIKE N'%Coquitlam%' OR MunicipalityName LIKE N'%Westminster%'
      OR MunicipalityName LIKE N'%Langley%' OR MunicipalityName LIKE N'%Delta%'
      OR MunicipalityName LIKE N'%Maple Ridge%' OR MunicipalityName LIKE N'%Port Moody%'
      OR MunicipalityName LIKE N'%White Rock%' OR MunicipalityName LIKE N'%Pitt Meadows%'
      OR MunicipalityName LIKE N'%Mission%' OR MunicipalityName LIKE N'%Chilliwack%'
      OR MunicipalityName LIKE N'%Abbotsford%' OR MunicipalityName LIKE N'%Hope%'
      OR MunicipalityName LIKE N'%Anmore%' OR MunicipalityName LIKE N'%Belcarra%'
      OR MunicipalityName LIKE N'%Lions Bay%' OR MunicipalityName LIKE N'%Bowen Island%'
      OR MunicipalityName LIKE N'%Squamish%' OR MunicipalityName LIKE N'%Whistler%'
      OR MunicipalityName LIKE N'%Pemberton%' OR MunicipalityName LIKE N'%Sechelt%'
      OR MunicipalityName LIKE N'%Powell River%' OR MunicipalityName LIKE N'%Gibsons%'
      OR MunicipalityName LIKE N'%Tsawwassen%' OR MunicipalityName LIKE N'%TFN%'
      OR MunicipalityName LIKE N'%UBC%' OR MunicipalityName LIKE N'%University Endowment%'
      OR MunicipalityName LIKE N'%Semiahmoo%'
        THEN N'Lower Mainland'
    ELSE RegionName
  END
WHERE Province = N'BC' AND NULLIF(LTRIM(RTRIM(RegionName)), '') IS NULL;

/* ---- Alberta -------------------------------------------------------- */
UPDATE opportunities.MajorProjectsInventory
SET RegionName =
  CASE
    /* Edmonton Metro (checked before Calgary; no shared substrings) */
    WHEN MunicipalityName LIKE N'%Edmonton%' OR MunicipalityName LIKE N'%St. Albert%'
      OR MunicipalityName LIKE N'%Strathcona County%' OR MunicipalityName LIKE N'%Sherwood Park%'
      OR MunicipalityName LIKE N'%Spruce Grove%' OR MunicipalityName LIKE N'%Stony Plain%'
      OR MunicipalityName LIKE N'%Leduc%' OR MunicipalityName LIKE N'%Fort Saskatchewan%'
      OR MunicipalityName LIKE N'%Beaumont%' OR MunicipalityName LIKE N'%Sturgeon County%'
      OR MunicipalityName LIKE N'%Parkland County%' OR MunicipalityName LIKE N'%Devon%'
      OR MunicipalityName LIKE N'%Morinville%' OR MunicipalityName LIKE N'%Bruderheim%'
      OR MunicipalityName LIKE N'%Gibbons%' OR MunicipalityName LIKE N'%Redwater%'
      OR MunicipalityName LIKE N'%Lamont%' OR MunicipalityName LIKE N'%Bon Accord%'
        THEN N'Edmonton Metro'
    /* Calgary Metro */
    WHEN MunicipalityName LIKE N'%Calgary%' OR MunicipalityName LIKE N'%Airdrie%'
      OR MunicipalityName LIKE N'%Cochrane%' OR MunicipalityName LIKE N'%Chestermere%'
      OR MunicipalityName LIKE N'%Okotoks%' OR MunicipalityName LIKE N'%Strathmore%'
      OR MunicipalityName LIKE N'%High River%' OR MunicipalityName LIKE N'%Rocky View%'
      OR MunicipalityName LIKE N'%Foothills%' OR MunicipalityName LIKE N'%Crossfield%'
      OR MunicipalityName LIKE N'%Balzac%' OR MunicipalityName LIKE N'%Wheatland%'
        THEN N'Calgary Metro'
    /* Southern Alberta */
    WHEN MunicipalityName LIKE N'%Lethbridge%' OR MunicipalityName LIKE N'%Medicine Hat%'
      OR MunicipalityName LIKE N'%Brooks%' OR MunicipalityName LIKE N'%Taber%'
      OR MunicipalityName LIKE N'%Coaldale%' OR MunicipalityName LIKE N'%Cardston%'
      OR MunicipalityName LIKE N'%Pincher Creek%' OR MunicipalityName LIKE N'%Fort Macleod%'
      OR MunicipalityName LIKE N'%Bow Island%' OR MunicipalityName LIKE N'%Raymond%'
      OR MunicipalityName LIKE N'%Magrath%' OR MunicipalityName LIKE N'%Picture Butte%'
      OR MunicipalityName LIKE N'%Crowsnest%' OR MunicipalityName LIKE N'%Redcliff%'
      OR MunicipalityName LIKE N'%Bassano%' OR MunicipalityName LIKE N'%Cypress County%'
      OR MunicipalityName LIKE N'%Newell%' OR MunicipalityName LIKE N'%Claresholm%'
      OR MunicipalityName LIKE N'%Suffield%' OR MunicipalityName LIKE N'%Nanton%'
        THEN N'Southern Alberta'
    /* Northern Alberta (NEW bucket — 141 had none for these) */
    WHEN MunicipalityName LIKE N'%Grande Prairie%' OR MunicipalityName LIKE N'%Wood Buffalo%'
      OR MunicipalityName LIKE N'%Fort McMurray%' OR MunicipalityName LIKE N'%Cold Lake%'
      OR MunicipalityName LIKE N'%Bonnyville%' OR MunicipalityName LIKE N'%Lloydminster%'
      OR MunicipalityName LIKE N'%Hinton%' OR MunicipalityName LIKE N'%Edson%'
      OR MunicipalityName LIKE N'%Whitecourt%' OR MunicipalityName LIKE N'%Peace River%'
      OR MunicipalityName LIKE N'%Grande Cache%' OR MunicipalityName LIKE N'%Slave Lake%'
      OR MunicipalityName LIKE N'%High Level%' OR MunicipalityName LIKE N'%High Prairie%'
      OR MunicipalityName LIKE N'%Athabasca%' OR MunicipalityName LIKE N'%St. Paul%'
      OR MunicipalityName LIKE N'%Fox Creek%' OR MunicipalityName LIKE N'%Swan Hills%'
      OR MunicipalityName LIKE N'%Manning%' OR MunicipalityName LIKE N'%Fairview%'
      OR MunicipalityName LIKE N'%Grimshaw%' OR MunicipalityName LIKE N'%Valleyview%'
      OR MunicipalityName LIKE N'%Beaverlodge%' OR MunicipalityName LIKE N'%Smoky Lake%'
      OR MunicipalityName LIKE N'%Smoky River%' OR MunicipalityName LIKE N'%Lac La Biche%'
      OR MunicipalityName LIKE N'%Yellowhead County%' OR MunicipalityName LIKE N'%Greenview%'
      OR MunicipalityName LIKE N'%Mackenzie County%' OR MunicipalityName LIKE N'%Saddle Hills%'
      OR MunicipalityName LIKE N'%Clear Hills%' OR MunicipalityName LIKE N'%Barrhead%'
      OR MunicipalityName LIKE N'%Westlock%' OR MunicipalityName LIKE N'%Rainbow Lake%'
      OR MunicipalityName LIKE N'%Vermilion River%' OR MunicipalityName LIKE N'%Wainwright%'
        THEN N'Northern Alberta'
    /* Central Alberta */
    WHEN MunicipalityName LIKE N'%Red Deer%' OR MunicipalityName LIKE N'%Sylvan Lake%'
      OR MunicipalityName LIKE N'%Lacombe%' OR MunicipalityName LIKE N'%Olds%'
      OR MunicipalityName LIKE N'%Innisfail%' OR MunicipalityName LIKE N'%Wetaskiwin%'
      OR MunicipalityName LIKE N'%Camrose%' OR MunicipalityName LIKE N'%Stettler%'
      OR MunicipalityName LIKE N'%Ponoka%' OR MunicipalityName LIKE N'%Rocky Mountain House%'
      OR MunicipalityName LIKE N'%Rimbey%' OR MunicipalityName LIKE N'%Blackfalds%'
      OR MunicipalityName LIKE N'%Didsbury%' OR MunicipalityName LIKE N'%Drumheller%'
      OR MunicipalityName LIKE N'%Three Hills%' OR MunicipalityName LIKE N'%Bowden%'
      OR MunicipalityName LIKE N'%Sundre%' OR MunicipalityName LIKE N'%Drayton Valley%'
      OR MunicipalityName LIKE N'%Mountain View%' OR MunicipalityName LIKE N'%Kneehill%'
      OR MunicipalityName LIKE N'%Clearwater County%' OR MunicipalityName LIKE N'%Flagstaff%'
      OR MunicipalityName LIKE N'%Vegreville%' OR MunicipalityName LIKE N'%Vermilion%'
      OR MunicipalityName LIKE N'%Provost%' OR MunicipalityName LIKE N'%Stettler%'
        THEN N'Central Alberta'
    ELSE RegionName
  END
WHERE Province = N'AB' AND NULLIF(LTRIM(RTRIM(RegionName)), '') IS NULL;
GO
