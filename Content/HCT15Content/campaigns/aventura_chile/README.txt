HCT 1.6 - Aventura Chile
========================
Chilean challenges (retos) in the spirit of the retail Adventures mode, as an extra campaign: ten chapters of
five rounds, each a lap with a rule - no nitro, low fuel (fuel cans only, no pits), five laps, taxi only,
micro only (micros in every livery against each other - the opener, Cap. 1.1 Arica, is one), no HUD, time trial
without rivals - on the laps of the Chile campaign, with the special skies
(aurora in Coyhaique and Punta Arenas, volcanic ash in Calama, Chillan, Temuco, Padre Las Casas, Villarrica
and Puerto Montt, nightfall in La Serena) where they fit. A round opens with a top-3 finish in the one before.
This is the interim version (2026-09-24): new touristic tracks and prize cars are the plan in
..\AVENTURA-CHILE-PROPUESTA.txt.
Rebuild:  python Tools\aventura_chile_build.py   (chapters and rules are the CHAPTERS / RULES tables in it)
Rules per round in campaign.json: Cars, NoRivals, NoFuelPickups, NoNitroPickups, Fuel, Nitros, NoHud, Difficulty
(HCT15\F1Campaign.cs F1Round; applied by ModCampaign.BuildData / ApplyRoundRules and ExtraCampaignGameModeData).
