with open('Assets/Scenes/01Day.unity', 'r', encoding='utf-8') as f:
    text = f.read()

replacements = {
    'tierName: Curb-Find Sanyo (Missing Remote)': 'tierName: Dumpster Sanyo',
    'tierName: Dusty Pawnshop Apex': 'tierName: Pawnshop Apex',
    'tierName: Taped-Together Magnavox': 'tierName: Taped Magnavox',
    'tierName: Overclocked 3AM Insomniac Deck': 'tierName: Overclocked Deck',
    'tierName: Eviction Auction Trinitron Companion': "tierName: Repo'd Trinitron",
    'tierName: Smoking Circuitboard Turbo-Box': 'tierName: Smoking Turbo',
    'tierName: Bribe-To-The-Landlord Hi-Fi': 'tierName: Landlord Bribe',
    'tierName: Gold-Plated Repo\'d Panasonic': 'tierName: Gold Panasonic',
    'tierName: Unlicensed Croatian LaserDisc Monolith': 'tierName: Bootleg Monolith',
    'tierName: Industrial Doom Accelerator 9000': 'tierName: Doom Accelerator',

    'tierName: Blank Memorex CD-R (Sharpie Scribbles)': 'tierName: Sharpie CDR',
    'tierName: Bargain Bin Kung Fu IV (Polish Dub)': 'tierName: Bargain Kung-Fu',
    'tierName: Subprime Mortgage Seminar 2006': 'tierName: Mortgage Seminar',
    'tierName: Late-Night Psychic Hotline Archives': 'tierName: Psychic Hotline',
    'tierName: Caught Cheating on Dashcam Vol. 7': 'tierName: Dashcam Cheaters',
    'tierName: Bass Fishing & Deep Personal Regret': 'tierName: Depressed Fishing',
    'tierName: Banned Energy Drink Commercials \'03': 'tierName: Banned Commercials',
    'tierName: Foreclosed McMansion ASMR Tour': 'tierName: Foreclosure Tour',
    'tierName: Underground Warehouse Rave Bootleg': 'tierName: Warehouse Bootleg',
    'tierName: Corporate Executive Severance Leak': 'tierName: Executive Severance'
}

for old, new in replacements.items():
    if old not in text:
        print('MISSING:', old)
    else:
        text = text.replace(old, new)

with open('Assets/Scenes/01Day.unity', 'w', encoding='utf-8') as f:
    f.write(text)

print('Updated 01Day.unity tier names to 2-word names successfully!')
