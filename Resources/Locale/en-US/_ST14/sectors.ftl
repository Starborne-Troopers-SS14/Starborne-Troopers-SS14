st14-sector-troopers-won = The troopers cleared the sector: the hive is destroyed.
st14-sector-hive-won = The hive held the sector: the troopers are wiped out.

st14-sector-template-command-description = Makes a blank 200x200 sector map with the sandstorm all around, the landing zone on the left and the hive on the right, and saves it into the server's data folder.
st14-sector-template-command-help = Usage: st14_sector_template [path] [tile]. Defaults to /st14_sector_template.yml and ST14FloorDunes.
st14-sector-template-command-no-tile = There is no tile { $tile }.
st14-sector-template-command-failed = Could not save { $path }.
st14-sector-template-command-done = Sector template saved: { $path }.

st14-sector-time-up = Time ran out: the attack has failed.
st14-sector-gate-closed = The storm closes this way off.
st14-sector-gate-not-initialized = Sector { $code } has not been fought over yet, there is no way through.
ent-ST14SectorGateBase = sector passage
    .desc = Beyond the storm lies the next sector. It can only be walked into once the sector has been fought over.

## Subsectors: a sector belongs to whoever has a building on every one of its resource points
st14-nest-no-point = There is no resource point next to you.
st14-nest-already = A nest already grows on this point.
st14-nest-enemy = A human drill holds this point. Break it first.
st14-nest-start = A nest starts to grow over the resource point.
st14-nest-done = The nest has grown: this part of the sector belongs to the hive.

## The side for the war over a region
st14-side-title = Your side
st14-side-none = No side chosen.
    It cannot be changed until the region is taken.
st14-side-chosen = You fight for { $side } until the region is taken.
st14-side-chosen-region = You fight for { $side } until region { $region } is taken.
st14-side-federation = the Federation
st14-side-arachnids = the arachnids
st14-side-pick-federation = For the Federation
st14-side-pick-arachnids = For the arachnids

st14-side-confirm = Sure? { $side }
