"""Deterministic, realistic-looking Kenyan demo people for the demo data pack.

    people_for(sim_ids, tiers) -> {sim_id: {"name", "email", "job_role"}}

A random generator seeded by the SIM id picks the name and the job role, so
every run gives the same people. Full names are unique across the set. Emails
are first.last@example.com in lower case, ASCII only, with a short numeric
suffix only when two different names would give the same address. All names
are invented combinations; they are not real people. Standard library only.
"""

from __future__ import annotations

import random

EMAIL_DOMAIN = "example.com"

# First names by community, plus common international ones. ASCII only, one word each.
FIRST_NAMES = (
    # Kikuyu
    "Wanjiru", "Njeri", "Wambui", "Nyambura", "Wangari", "Muthoni", "Wairimu", "Njoki", "Kamau", "Mwangi",
    "Kariuki", "Gitau", "Maina", "Kimani", "Njoroge", "Karanja", "Macharia", "Waweru", "Githinji", "Ndungu",
    # Luo
    "Achieng", "Akinyi", "Adhiambo", "Atieno", "Awino", "Anyango", "Apiyo", "Otieno", "Odhiambo", "Ochieng",
    "Onyango", "Omondi", "Owino", "Okoth", "Ouma", "Oloo", "Odongo", "Juma", "Abuya", "Ogola",
    # Luhya
    "Nafula", "Naliaka", "Nekesa", "Nanjala", "Khasiala", "Wekesa", "Wafula", "Simiyu", "Barasa", "Mukhwana",
    "Namukhula", "Makokha", "Wanyama", "Sifuna", "Masinde", "Khaemba", "Mulama", "Shikuku", "Nasimiyu", "Lusweti",
    # Kalenjin
    "Jepchirchir", "Chebet", "Jeptoo", "Cheptoo", "Chepkoech", "Jebet", "Kiprono", "Kipchoge", "Kiplagat", "Kiprotich",
    "Kipkemboi", "Kibet", "Kiptoo", "Rotich", "Kosgei", "Koech", "Kirui", "Cheruiyot", "Langat", "Biwott",
    # Kamba
    "Mutheu", "Mwikali", "Nduku", "Syokau", "Kavata", "Mumbua", "Musyoka", "Mutua", "Kioko", "Muema",
    "Mwendwa", "Kilonzo", "Ndambuki", "Kithuka", "Muthama", "Nzioka", "Wambua", "Mulwa", "Kyalo", "Katiwa",
    # Kisii
    "Moraa", "Kemunto", "Bonareri", "Nyaboke", "Kwamboka", "Mokeira", "Nyakerario", "Ondieki", "Nyamweya", "Ombati",
    "Mogaka", "Momanyi", "Ongera", "Obino", "Mayaka", "Bosire", "Oroko", "Nyabuto", "Gesare", "Kerubo",
    # Meru
    "Kananu", "Gatwiri", "Karimi", "Kawira", "Mukami", "Kaimenyi", "Mutwiri", "Kinoti", "Gitonga", "Mwiti",
    "Murithi", "Kirimi", "Muriungi", "Mugambi", "Thuranira", "Kiambi", "Ntinyari", "Citsiri", "Mworia", "Kubai",
    # Coastal
    "Zawadi", "Fatuma", "Mwanaisha", "Zainabu", "Amina", "Halima", "Rehema", "Mwanasha", "Salim", "Hamisi",
    "Mwinyi", "Bakari", "Said", "Omar", "Rashid", "Mwanahamisi", "Kahindi", "Katana", "Baya", "Charo",
    # Somali
    "Hodan", "Amran", "Faduma", "Ifrah", "Sagal", "Ayaan", "Khadija", "Abdi", "Hassan", "Mohamed",
    "Abdullahi", "Yusuf", "Ismail", "Farah", "Warsame", "Liban", "Guled", "Dahir", "Ahmed", "Nasra",
    # Kenyan-Asian
    "Priya", "Anita", "Meera", "Kavita", "Sunita", "Neha", "Pooja", "Rajesh", "Amit", "Sanjay",
    "Vijay", "Nilesh", "Hitesh", "Jignesh", "Kiran", "Ashok", "Bhavin", "Dipti", "Hetal", "Rupal",
    # International
    "Grace", "Faith", "Mercy", "Joyce", "Esther", "Ruth", "Lucy", "Mary", "Sarah", "Caroline",
    "Elizabeth", "Beatrice", "Irene", "Cynthia", "Brenda", "Linet", "Naomi", "Purity", "Winnie", "Gladys",
    "John", "David", "Peter", "James", "Daniel", "Samuel", "Joseph", "Michael", "Brian", "Kevin",
    "Dennis", "Collins", "Victor", "Felix", "Eric", "Stephen", "Paul", "George", "Martin", "Allan",
)

LAST_NAMES = (
    # Kikuyu
    "Mwangi", "Kamau", "Njoroge", "Kariuki", "Gathogo", "Wainaina", "Mugo", "Ngugi", "Kinyanjui", "Gachanja",
    "Wachira", "Muriuki", "Githae", "Nganga", "Thuku", "Mbugua", "Gikonyo", "Kihara", "Muturi", "Wambugu",
    # Luo
    "Otieno", "Odhiambo", "Ochieng", "Onyango", "Omondi", "Owino", "Okoth", "Ouma", "Oloo", "Odongo",
    "Ogutu", "Ayieko", "Opiyo", "Okeyo", "Obuya", "Oduor", "Orwa", "Ojwang", "Nyawade", "Agola",
    # Luhya
    "Wekesa", "Wafula", "Simiyu", "Barasa", "Mukhwana", "Makokha", "Wanyama", "Sifuna", "Masinde", "Khaemba",
    "Mulama", "Shikuku", "Lusweti", "Wangila", "Nyongesa", "Juma", "Mudavadi", "Namwamba", "Ambundo", "Mmbone",
    # Kalenjin
    "Kiprono", "Kipchoge", "Kiplagat", "Kiprotich", "Kipkemboi", "Kibet", "Kiptoo", "Rotich", "Kosgei", "Koech",
    "Kirui", "Cheruiyot", "Langat", "Biwott", "Sang", "Tanui", "Bett", "Chepkwony", "Ruto", "Kimutai",
    # Kamba
    "Musyoka", "Mutua", "Kioko", "Muema", "Mwendwa", "Kilonzo", "Ndambuki", "Kithuka", "Muthama", "Nzioka",
    "Wambua", "Mulwa", "Kyalo", "Mwololo", "Mutisya", "Maundu", "Nzuki", "Kitheka", "Ngao", "Mbithi",
    # Kisii
    "Ondieki", "Nyamweya", "Ombati", "Mogaka", "Momanyi", "Ongera", "Obino", "Mayaka", "Bosire", "Oroko",
    "Nyabuto", "Nyakundi", "Ogeto", "Mokua", "Onsongo", "Nyandika", "Gekonge", "Mochache", "Ratemo", "Machuki",
    # Meru
    "Kaimenyi", "Mutwiri", "Kinoti", "Gitonga", "Mwiti", "Murithi", "Kirimi", "Muriungi", "Mugambi", "Thuranira",
    "Kiambi", "Mworia", "Kubai", "Rimberia", "Mutuma", "Kiogora", "Gichuru", "Ikiara", "Mberia", "Marete",
    # Coastal
    "Mwinyi", "Bakari", "Said", "Omar", "Rashid", "Hamisi", "Kahindi", "Katana", "Baya", "Charo",
    "Mwachofi", "Mwarandu", "Mbwana", "Chengo", "Shariff", "Salim", "Athman", "Mazrui", "Nyale", "Kombe",
    # Somali
    "Abdi", "Hassan", "Mohamed", "Abdullahi", "Yusuf", "Ismail", "Farah", "Warsame", "Guled", "Dahir",
    "Ahmed", "Osman", "Ali", "Hussein", "Adan", "Duale", "Sheikh", "Mahat", "Noor", "Aden",
    # Kenyan-Asian
    "Patel", "Shah", "Mehta", "Desai", "Gandhi", "Joshi", "Kapoor", "Sharma", "Chauhan", "Vora",
    "Thakkar", "Parekh", "Modi", "Raval", "Pandya", "Lakhani", "Kotecha", "Varsani", "Gohil", "Solanki",
)

JOB_ROLES = {
    "field": (
        "Field Technician", "Senior Field Technician", "Network Installer", "Site Engineer",
        "Maintenance Technician", "Installation Lead", "Fibre Splicer", "Field Operations Officer",
    ),
    "manager": (
        "Operations Manager", "Regional Manager", "Branch Manager", "Head of Department",
        "Finance Manager", "Programme Manager", "Area Manager", "Country Director",
    ),
    "professional": (
        "Consultant", "Senior Consultant", "Business Analyst", "Solutions Architect",
        "Project Coordinator", "Software Engineer", "Data Analyst", "Account Executive",
    ),
    "support": (
        "Customer Support Agent", "Support Team Lead", "Helpdesk Officer", "Customer Care Representative",
        "Technical Support Specialist", "Call Centre Agent", "Service Desk Analyst", "Client Success Officer",
    ),
}


def _slug(word: str) -> str:
    return "".join(ch for ch in word.lower() if "a" <= ch <= "z")


def people_for(sim_ids, tiers: dict[int, str]) -> dict[int, dict[str, str]]:
    """Return {sim_id: {name, email, job_role}} in ascending SIM id order."""
    used_names: set[str] = set()
    used_emails: set[str] = set()
    people: dict[int, dict[str, str]] = {}
    for sim_id in sorted(sim_ids):
        rng = random.Random(f"simguard-demo-person-{sim_id}")  # str seeds are stable across runs
        while True:
            first, last = rng.choice(FIRST_NAMES), rng.choice(LAST_NAMES)
            name = f"{first} {last}"
            if first != last and name not in used_names:
                break
        used_names.add(name)
        base = f"{_slug(first)}.{_slug(last)}"
        email = f"{base}@{EMAIL_DOMAIN}"
        suffix = 1
        while email in used_emails:
            suffix += 1
            email = f"{base}{suffix}@{EMAIL_DOMAIN}"
        used_emails.add(email)
        role = rng.choice(JOB_ROLES[tiers[sim_id]])
        people[sim_id] = {"name": name, "email": email, "job_role": role}
    return people
