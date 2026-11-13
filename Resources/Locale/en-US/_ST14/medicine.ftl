ent-ST14FieldDefibrillator = field defibrillator
    .desc = Charge it on the patient, fire it in the green. Shout before you do.

st14-heartbeat-hint = Stay with us! Press {$key} on the beat
st14-heartbeat-revive = NOW! Press {$key} on the beat!

st14-field-defib-charging = Charging!
st14-field-defib-clear = Clear!
st14-field-defib-hint = Click the patient again in the green zone
st14-field-defib-not-needed = This one does not need a shock.
st14-field-defib-broken = The charge is lost!
st14-field-defib-overload = The defibrillator overloads!

st14-medicine-too-late = It's too late, they can't be brought back.

## Body parts
st14-zone-head = head
st14-zone-torso = chest
st14-zone-armleft = left arm
st14-zone-armright = right arm
st14-zone-legleft = left leg
st14-zone-legright = right leg

## Wounds
st14-wound-fracture-self = CRACK! A bone is broken: { $zone }!
st14-wound-fracture-others = A bone cracks: { $victim } doubles over in pain.
st14-wound-arterial-self = Blood spurts out, an artery is cut: { $zone }!
st14-bleeding-gushing = Blood is gushing: { $zone }!
st14-pain-unbearable = UNBEARABLE PAIN!
st14-pain-groan = groans in pain
st14-pain-shock = Pain shock! Your legs give way.
st14-heart-stopped-self = Your heart stutters... everything goes dark...
st14-heart-stopped-others = { CAPITALIZE($victim) } wheezes and goes limp: the heart has stopped!
st14-heart-restarted = The heart beats again!
st14-tourniquet-ischemia = Everything under the tourniquet goes numb and cold: { $zone }. A medic is needed!
st14-tourniquet-necrosis-self = Everything under the tourniquet has died: { $zone } is no longer yours.
st14-tourniquet-necrosis-others = The dead limb under the tourniquet of { $victim } falls away.

## Drugs
reagent-name-st14-morphine = morphine
reagent-desc-st14-morphine = A strong painkiller. Takes away pain and screaming, in a large dose it slows the breathing. The antidote is naloxone.
reagent-name-st14-naloxone = naloxone
reagent-desc-st14-naloxone = Tears morphine out of the blood: ends an overdose, and all the pain comes back at once.
reagent-name-st14-combat-stim = combat stimulant
reagent-desc-st14-combat-stim = Fast legs, a steady hand, wounds do not slow you. Loads the heart and ends in a crash. Dangerous with adrenaline.
reagent-name-st14-ketamine = ketamine
reagent-desc-st14-ketamine = Field anaesthesia: the patient sleeps and feels nothing while there is enough of it.
st14-morphine-effect-warm = Warm cotton spreads through your body.
st14-morphine-effect-far = The pain is somewhere far away, as if it were not yours.
st14-naloxone-effect = An icy wave rolls through your veins.
st14-stim-effect-heart = Your heart pounds against your ribs.
st14-stim-effect-sharp = Everything around turns sharp and slow.
st14-ketamine-effect = The world drifts off to the side.
st14-drug-naloxone = The morphine haze tears apart, and the pain comes back all at once!
st14-drug-crash = The stimulant lets go. Your body turns to lead.
st14-drug-morphine-overdose = Your breathing grows slow and shallow...

## Medic
st14-verb-category-medicine = Medicine
st14-treat-tourniquet = Apply a tourniquet
st14-treat-bandage = Dress the wound
st14-treat-hemostatic = Pack the wound
st14-treat-splint = Apply a splint
st14-treat-suture = Suture
st14-treat-verb = { $action }: { $zone }
st14-treat-nothing = There is nothing here for { $item }.
st14-treat-lie-down = The patient has to lie down.
st14-treat-start-self = { $action }: { $zone }...
st14-treat-start-others = { CAPITALIZE($user) } treats { $target }: { $action }, { $zone }.
st14-treat-done-tourniquet = Tourniquet tight: { $zone }. The bleeding stopped.
st14-treat-done-bandage = Dressed: { $zone }.
st14-treat-done-hemostatic = Packed: { $zone }.
st14-treat-done-splint = Splinted: { $zone }.
st14-treat-done-suture = Sutured: { $zone }.
st14-treat-remove-tourniquet = Remove tourniquet: { $zone } ({ $minutes } min)
st14-treat-removing = You loosen the tourniquet: { $zone }...
st14-treat-removed = Tourniquet removed: { $zone }.
st14-treat-removed-bleeding = Tourniquet removed, and the bleeding starts again: { $zone }!
st14-treat-pulse = Check pulse
st14-treat-cpr = Chest compressions
st14-pulse-none = No pulse!
st14-pulse-reading = Pulse { $pulse }: { $kind }.
st14-pulse-steady = steady
st14-pulse-fast = fast
st14-pulse-racing = racing
st14-pulse-weak = weak
st14-pulse-thready = thready, barely there
st14-pulse-erratic = erratic, skipping beats
st14-cpr-start = { CAPITALIZE($user) } pushes on the chest of { $target }: one, two, three...
st14-medic-call-shout = MEDIC!
st14-medic-call-self = You call for a medic. Medics see a red cross over you.

## Examine
st14-examine-tourniquet = [color=#d8b26a]A tourniquet is on ({ $minutes } min): { $zone }.[/color]
st14-examine-tourniquet-long = [color=#e06040]A tourniquet has been on for { $minutes } min, the limb turns blue: { $zone }![/color]
st14-examine-stump = [color=#c03030]A stump: { $zone }.[/color]
st14-examine-gushing = [color=#ff3030]Blood is gushing: { $zone }![/color]
st14-examine-bleeding = [color=#c04040]Bleeding: { $zone }.[/color]
st14-examine-bandaged = Dressed: { $zone }.
st14-examine-splinted = Splinted: { $zone }.
st14-examine-broken = [color=#d08040]Bent the wrong way: { $zone }.[/color]
st14-examine-no-pulse = [color=#ff3030]The chest does not rise. No pulse![/color]

## Scanner
st14-scan-title = FIELD MEDICAL SCANNER
st14-scan-pulse = PULSE
st14-scan-blood = BLOOD
st14-scan-pain = PAIN
st14-scan-bleeding = BLEEDING
st14-scan-heart-load = HEART LOAD
st14-scan-drugs = IN THE BLOOD
st14-scan-no-drugs = Nothing
st14-scan-warnings = DANGER
st14-scan-all-clear = No threat to life
st14-scan-wounds = WOUNDS
st14-scan-no-wounds = No wounds
st14-scan-out-of-range = The patient is out of range
st14-scan-state-alive = Conscious
st14-scan-state-crit = Unconscious
st14-scan-state-dead = Dead
st14-scan-state-arrest = CARDIAC ARREST
st14-scan-per-second = { $value } u/s
st14-scan-tourniquet = tourniquet { $minutes }:{ $seconds }
st14-scan-missing = missing
st14-scan-wound-cut = cut
st14-scan-wound-puncture = gunshot
st14-scan-wound-burn = burn
st14-scan-wound-fracture = fracture
st14-scan-wound-stump = stump
st14-scan-wound-arterial = artery
st14-scan-wound-bandaged = dressed
st14-scan-wound-packed = packed
st14-scan-wound-sutured = sutured
st14-scan-wound-splinted = splint
st14-scan-warn-arrest = Cardiac arrest: compressions, adrenaline, defibrillator!
st14-scan-warn-bleeding = Heavy bleeding: tourniquet or packing!
st14-scan-warn-ischemia = Tourniquet on too long, the limb dies in { $left } min: { $zone }
st14-scan-warn-blood = Heavy blood loss: blood is needed
st14-scan-warn-arrhythmia = Arrhythmia: the heart may stop
st14-scan-warn-stim-adrenaline = Stimulant with adrenaline: risk of cardiac arrest
st14-scan-warn-speedball = Morphine with a stimulant: the heart is at its limit
st14-scan-warn-overdose = Overdose: { $drug }
st14-scan-warn-stump = The stump is not sutured
st14-scan-warn-fracture = A fracture without a splint
st14-scan-warn-anaesthesia = The patient is under anaesthesia
st14-scan-warn-crash = Crashing after a stimulant

## Torn limbs
st14-limb-arm-left = left arm
st14-limb-arm-right = right arm
st14-limb-leg-left = left leg
st14-limb-leg-right = right leg
st14-limb-arm-left-of = left arm
st14-limb-arm-right-of = right arm
st14-limb-leg-left-of = left leg
st14-limb-leg-right-of = right leg
st14-limb-torn-self = YOUR { $limb } IS TORN OFF!
st14-limb-torn-others = { CAPITALIZE($victim) } loses the { $limbOf }!

## Prostheses
st14-prosthesis-not-needed = { CAPITALIZE($target) } does not need this prosthesis.
st14-prosthesis-lie-down = A steel prosthesis is fitted lying down: put the patient down.
st14-prosthesis-suture-first = Suture the stump first, a prosthesis does not go onto an open wound.
st14-prosthesis-start = { CAPITALIZE($user) } starts fitting a prosthesis to the stump of { $target }.
st14-prosthesis-pain = THEY ARE DRIVING THE PROSTHESIS INTO YOUR STUMP!
st14-prosthesis-done = The prosthesis is in place.
st14-limbs-command-description = Debug medicine on your own body: tear a limb, fit a prosthesis, wound, stop the heart.
st14-limbs-command-help = st14_limbs tear [ArmLeft|ArmRight|LegLeft|LegRight] | fit <prosthesis> | wound <part> <cut|puncture|burn|fracture> | treat <treatment> <part> | drug <reagent> <u> | infect <part> <0..1> | arrest | scan
st14-limbs-command-nothing = Nothing happened.

## Job
st14-job-name-medic = Medic
st14-job-description-medic = The field medic of the Mobile Infantry: tourniquets, packing, anaesthesia and a heart to restart.
st14-job-supervisors-medic = the commander
st14-department-medical = Medical Corps
st14-department-medical-description = The medics of the Mobile Infantry.
st14-stack-bandage = field dressing
st14-stack-hemostatic = hemostatic gauze
st14-stack-splint = splint

## New drugs
reagent-name-st14-atropine = atropine
reagent-desc-st14-atropine = Speeds a slow heart, clears the morphine fog, pulls some bug venom out. With adrenaline the heart races.
reagent-name-st14-amiodarone = amiodarone
reagent-desc-st14-amiodarone = Keeps the heart in rhythm under any load and helps a shock or compressions start a stopped one.
reagent-name-st14-ketorolac = ketorolac
reagent-desc-st14-ketorolac = Takes pain away without clouding the head, but thins the blood.
reagent-name-st14-antibiotic = antibiotic
reagent-desc-st14-antibiotic = Slowly kills the infection of the wounds.
reagent-name-st14-antipyretic = fever reducer
reagent-desc-st14-antipyretic = Takes the fever and a little pain away. The infection goes on.
reagent-name-st14-osteogen = osteogen
reagent-desc-st14-osteogen = Grows a broken bone back in a minute and a half: straight under a splint, crooked without one.
reagent-name-st14-regenerol = regenerol
reagent-desc-st14-regenerol = Wounds close three times faster.
reagent-name-st14-antivenom = antivenom
reagent-desc-st14-antivenom = Binds bug venom and pulls it out of the blood.
reagent-name-st14-modafinil = modafinil
reagent-desc-st14-modafinil = Wakes the body up: the stimulant crash and the morphine fog go. One more load on the heart.
reagent-name-st14-plasma = plasma expander
reagent-desc-st14-plasma = Fills the vessels when there is no blood to give. It thins what blood is left.
reagent-name-st14-bug-venom = bug venom
reagent-desc-st14-bug-venom = The venom of tiger bug claws: limbs grow heavy and numb, the picture goes green, a lot of it stops the breath.
st14-atropine-effect = Your mouth goes dry, your heart speeds up.
st14-osteogen-effect = The bone aches and itches inside.
st14-modafinil-effect = Your head clears as if splashed with ice water.
st14-venom-effect-burn = The venom burns from inside.
st14-venom-effect-numb = Your fingers go numb and stop obeying.
st14-venom-breathing = Your chest tightens, every breath is harder!
st14-venom-injected = The claws were venomous: a burning spreads through your veins!
st14-drug-antidote-pain = The painkiller is torn out of your blood, and all the pain comes back at once!
st14-drug-ketorolac-overdose = A dull pain twists your stomach.
st14-drug-atropine-overdose = Your heart pounds like mad, the world swims.
st14-drug-amiodarone-overdose = Your heart beats slower and slower...
st14-drug-antipyretic-overdose = A dull heaviness under the right ribs.

## What a drug does (scanner)
st14-drug-summary-morphine = strong painkiller, fogs the head; chokes past 20 u
st14-drug-summary-ketorolac = painkiller without the fog, thins the blood
st14-drug-summary-ketamine = anaesthesia, feels nothing
st14-drug-summary-naloxone = pulls morphine out, the pain comes back
st14-drug-summary-adrenaline = keeps the heart going, helps compressions
st14-drug-summary-atropine = speeds the heart, clears the fog, pulls venom out
st14-drug-summary-amiodarone = keeps the rhythm, helps restart the heart
st14-drug-summary-stim = speed and a steady hand, then the crash
st14-drug-summary-modafinil = ends the crash and the fog
st14-drug-summary-tranexamic = the blood clots faster
st14-drug-summary-plasma = fills the blood, thins it
st14-drug-summary-antibiotic = kills infection
st14-drug-summary-antipyretic = takes the fever away, the infection goes on
st14-drug-summary-osteogen = grows bones back
st14-drug-summary-regenerol = wounds close three times faster
st14-drug-summary-antivenom = pulls bug venom out
st14-drug-summary-venom = venom: heaviness, numbness, choking
st14-drug-summary-alcohol = a little pain relief, thins the blood

## Interactions
st14-interaction-heart-pounding = Your heart pounds so hard it hurts.
st14-interaction-heavy-breath = Breathing is heavy, your chest barely rises.
st14-interaction-slow-heart = Your heart beats slow and heavy.
st14-interaction-bad-trip = The walls breathe, and someone is in every shadow.
st14-scan-warn-morphine-alcohol = Morphine with alcohol: breathing depressed
st14-scan-warn-morphine-venom = Morphine with venom: breathing stops
st14-scan-warn-ketorolac-tranexamic = Ketorolac cancels the hemostatic
st14-scan-warn-ketorolac-alcohol = Ketorolac with alcohol: the blood will not clot
st14-scan-warn-atropine-adrenaline = Atropine with adrenaline: the heart races
st14-scan-warn-atropine-amiodarone = Atropine and amiodarone cancel each other
st14-scan-warn-bradycardia = Amiodarone with morphine: the heart slows down
st14-scan-warn-modafinil-stim = Modafinil with a stimulant: heart overload
st14-scan-warn-ketamine-stim = Ketamine with a stimulant: delirium, heart load
st14-scan-warn-plasma-tranexamic = The plasma expander dilutes the hemostatic
st14-scan-warn-venom = Bug venom in the blood: antivenom is needed
st14-scan-warn-sepsis = Sepsis: the infection is in the blood, antibiotic needed
st14-scan-warn-infection = Infected wound: antiseptic and antibiotic
st14-scan-warn-infection-hidden = The fever is down, but the infection goes on: antibiotic needed
st14-scan-warn-sewn-dirty = A dirty wound is sewn shut: it rots under the stitches
st14-scan-warn-malunion = A bone grew back crooked: break and set it with the suture kit

## Infection and bones
st14-wound-infected = The wound is inflamed and throbbing: { $zone }.
st14-wound-sepsis = Chills and weakness: the infection is in the blood!
st14-bone-healed = The bone has grown back: { $zone }.
st14-bone-malunion = The bone grew back crooked: { $zone }. Nothing held it straight.
st14-fever-shiver = shakes with chills
st14-examine-infected = [color=#b5d94a]The wound is inflamed and swollen: { $zone }.[/color]
st14-examine-fever = [color=#e0a040]The face burns, sweat rolls down it.[/color]
st14-scan-wound-malunion = crooked bone
st14-scan-wound-infected = infection { $percent }%
st14-scan-wound-clean = cleaned
st14-scan-wound-dirty = dirty
st14-scan-wound-healing = healing { $percent }%
st14-treat-disinfect = Apply antiseptic
st14-treat-done-disinfect = Cleaned: { $zone }.
st14-stack-antiseptic = antiseptic

## Own adrenaline and hemostatic instead of the station drugs, the blood bag

reagent-name-st14-adrenaline = adrenaline
reagent-desc-st14-adrenaline = Keeps the heart going and helps compressions start a stopped one. Heals no wounds by itself.
reagent-name-st14-tranexamic = hemostatic
reagent-desc-st14-tranexamic = Tranexamic acid: the blood clots faster, small wounds close by themselves.
st14-treat-transfusion = Give blood
st14-treat-done-transfusion = Blood given.

## Medic calls

st14-medic-call-done = { $name } needs no medic any more.
st14-medic-respond-cancel = You no longer go to { $name }.
st14-medic-respond-gone = The medic is not coming. Call again.
st14-medic-respond-none = Nobody calls for a medic.
st14-medic-respond-self = You go to { $name }.
st14-medic-respond-patient = Medic { $name } is coming to you!
st14-medic-status-waiting = You are calling for a medic. Nobody answered yet
st14-medic-status-coming = Medic { $name } is coming: { $distance } m
st14-medic-status-coming-far = Medic { $name } is coming
st14-medic-hint-open = Calls for a medic: { $count }  ·  { $key }: go to the nearest
st14-medic-hint-going = You go to { $name }: { $distance } m  ·  { $key }: cancel
st14-medic-mark = { $name } · { $distance } m
st14-medic-mark-taken = { $name } · { $distance } m · { $medic } goes
st14-medic-mark-mine = { $name } · { $distance } m · you go
ui-options-function-st14-medic-respond = Medic: answer a call
st14-heartbeat-hit = ON BEAT!
st14-heartbeat-miss = MISS
