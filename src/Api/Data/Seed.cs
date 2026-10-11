using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Fieldroom;

public static class Seed
{
    public static async Task Run(IServiceProvider services, string? bootstrapPassword)
    {
        var db = services.GetRequiredService<LibraryDb>();
        if (!await db.Policies.AnyAsync()) { db.Policies.Add(new LendingPolicy()); await db.SaveChangesAsync(); }
        var products = new (string Name, string Category, string Image, string Description, string Specs, string Accessories, int Copies)[]
        {
            ("The everyday mirrorless", "Cameras", "camera", "A quietly capable camera for documenting the things you make. Crisp stills, considered controls and a compact body that feels at home on a walk or in a workshop.", "24 MP · APS-C sensor · 4K video · 510 g", "35 mm lens, two batteries, charger, strap, padded case", 2),
            ("Pocket film companion", "Cameras", "compact", "A pocketable digital camera with a fixed lens and warm colour profiles. Keep your attention on the moment, not a bag of lenses.", "20 MP · 28 mm equivalent · 290 g", "Battery, USB-C cable, wrist strap, soft pouch", 2),
            ("Studio light kit", "Cameras", "light", "Soft, reliable light for portraits, tabletop projects and small productions. Two compact panels make a versatile starting point.", "2 × 60 W · 2700–6500 K · CRI 95", "Two panels, stands, diffusers, power supplies, carry bag", 1),
            ("Carbon travel tripod", "Cameras", "tripod", "A stable foundation for careful compositions, long exposures and a small video setup. Packs down neatly for the journey.", "1.4 kg · 150 cm maximum · 8 kg payload", "Ball head, quick-release plate, hex key, carry sleeve", 2),
            ("Directional microphone", "Cameras", "microphone", "Capture clearer voices and atmosphere on location. A compact directional microphone for interviews and short films.", "Supercardioid · 3.5 mm output · 120 g", "Shock mount, windscreen, two cables, case", 1),
            ("Cordless drill & driver", "Tools", "drill", "For shelves, small repairs and the project you have been meaning to finish. Balanced, controllable and ready for a productive afternoon.", "18 V · 50 Nm · 13 mm chuck · 1.6 kg", "Two batteries, charger, 20-piece bit set, hard case", 3),
            ("Random orbital sander", "Tools", "sander", "Bring rough timber to a smooth, tactile finish. Dust collection keeps the workspace a little calmer.", "125 mm disc · variable speed · 280 W", "Dust bag, extraction adapter, case; bring abrasive discs", 2),
            ("Precision jigsaw", "Tools", "jigsaw", "Curves, cutouts and careful straight cuts in timber. A compact saw for patient, well-marked work.", "650 W · 80 mm timber depth · bevel base", "Wood blade, guide, hex key, case; eye protection required", 1),
            ("Workshop clamp set", "Tools", "clamps", "An extra pair of hands for glue-ups, assemblies and getting everything square. Four sturdy one-handed clamps.", "4 × 300 mm · 150 kg clamping force", "Four clamps, protective jaw pads, storage roll", 2),
            ("Laser measure", "Tools", "measure", "Measure rooms and materials with less fuss. A readable display and simple controls for planning your next build.", "40 m range · ±2 mm · area and volume", "Two batteries, belt pouch, quick-start card", 1),
            ("Two-person trail tent", "Outdoors", "tent", "A small shelter for a bigger weekend. Lightweight, easy to pitch and comfortable for two people travelling lightly.", "2 person · 2.3 kg · three season · 3000 mm fly", "Inner, fly, poles, pegs, footprint, repair sleeve, stuff sack", 2),
            ("Weekend hiking pack", "Outdoors", "pack", "Room for the essentials, with a supportive back system and practical pockets. Built for a few days away from the usual routine.", "55 L · adjustable torso · 1.7 kg", "Rain cover, removable hip belt, pack liner", 2),
            ("Camp cooking set", "Outdoors", "cookset", "A compact nesting set for a proper meal outside. Thoughtful handles and just enough capacity for two.", "1.8 L pot · 1 L pan · anodised aluminium · 620 g", "Pot, pan, lid, two bowls, folding spoons, mesh bag; no fuel", 2),
            ("Rechargeable lantern", "Outdoors", "lantern", "Warm light for a campsite table or an evening under the trees. A gentle dimmer makes it useful long after dinner.", "400 lm · USB-C · IPX4 · up to 40 hours", "Charging cable, hanging hook, padded sleeve", 3),
            ("Insulated sleeping mat", "Outdoors", "mat", "A little more comfort between you and the ground. Packs small and adds insulation on cool nights.", "R-value 4.2 · 183 × 55 cm · 540 g", "Pump sack, repair patches, storage bag", 2)
        };
        var index = 0;
        foreach (var r in products)
        {
            index++;
            var id = Guid.Parse($"10000000-0000-0000-0000-{index:000000000000}");
            if (await db.Products.AnyAsync(x => x.Id == id)) continue;
            var p = new Product { Id = id, Name = r.Name, Category = r.Category, Description = r.Description, Specifications = r.Specs, Accessories = r.Accessories, Image = r.Image };
            for (var copy = 1; copy <= r.Copies; copy++) p.Assets.Add(new Asset { Id = Guid.Parse($"20000000-0000-0000-{index:0000}-{copy:000000000000}"), Tag = $"FR-{index:000}-{copy:00}", Condition = copy == 1 ? "Excellent · recently inspected" : "Good · light cosmetic wear" });
            db.Products.Add(p);
        }
        await db.SaveChangesAsync();
        if (string.IsNullOrWhiteSpace(bootstrapPassword)) return;
        var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roles.RoleExistsAsync("Manager")) await roles.CreateAsync(new IdentityRole("Manager"));
        var users = services.GetRequiredService<UserManager<Member>>();
        const string email = "desk@fieldroom.example";
        var manager = await users.FindByEmailAsync(email);
        if (manager == null)
        {
            manager = new Member { UserName = email, Email = email, DisplayName = "Morgan · lending desk" };
            var result = await users.CreateAsync(manager, bootstrapPassword);
            if (!result.Succeeded) throw new InvalidOperationException("Local bootstrap password does not meet Identity policy.");
            await users.AddToRoleAsync(manager, "Manager");
        }
    }
}
