using FlareQuotes.Core.Models;

namespace FlareQuotes.Core.Features;

public static class FeatureCatalog
{
    // Mirrors the current Python app's ADDITIONAL_FEATURE_OPTIONS_BY_TEMPLATE behavior.
    // Indoor Outdoor See Through is treated like Indoor See Through for feature availability.
    public static readonly FeatureOption[] All = [
        // Keep this order intentional. The dropdown should follow the quote workflow, not alphabetical sorting.
        new("summit_burner", "Summit Burner", "Driftwood In-Log Elevated Burner System", IndoorModern(), true, "",
            ["summit burner", "summit"]),
        new("double_glass", "Double Glass Safety Barrier", "Crystal Clear Double Glass Safety Barrier",
            Types(FireplaceType.Indoor, FireplaceType.IndoorSeeThrough, FireplaceType.IndoorOutdoorSeeThrough,
                  FireplaceType.Traditional),
            true, "", ["double glass", "safety barrier", "double glass safety barrier"]),
        new("reflective_black_back", "Reflective Black Back", "Black Glass Back That Reflects the Flame and Media",
            Types(FireplaceType.Indoor, FireplaceType.Outdoor), true, "",
            ["reflective black back", "reflective back", "black back"]),
        new("reflective_black_sides", "Reflective Black Sides", "Black Glass Sides That Reflects the Flame and Media",
            Types(FireplaceType.Indoor, FireplaceType.IndoorSeeThrough, FireplaceType.IndoorOutdoorSeeThrough,
                  FireplaceType.Outdoor, FireplaceType.OutdoorSeeThrough),
            false, "", ["reflective sides", "reflective black sides", "reflective side panels", "side panels"]),
        new("rgb_leds", "RGB LEDs", "RGB Lighting Adds Playful Accent or Natural Glow Inside", IndoorModern(), true, "",
            ["rgb", "rgb led", "rgb leds", "leds", "multi color", "multi-color"]),
        new("summer_kit", "Summer Kit", "Heat Removal Wired to a Switch",
            Types(FireplaceType.Indoor, FireplaceType.IndoorSeeThrough, FireplaceType.IndoorOutdoorSeeThrough,
                  FireplaceType.Traditional, FireplaceType.Large),
            false, "", ["summer kit", "heat removal fan", "summer"]),
        new("active_heat_flex", "Active Heat Flex", "Always Remove Heat to Remove The Heat Release", IndoorModern(),
            false, "Indoor Price Book", ["active heat flex", "active heat", "active flex"]),
        new("passive_heat_flex", "Passive Heat Flex", "Keep it Simple Ducted Heat Release Register and Plenum",
            IndoorModern(), false, "Indoor Price Book", ["passive heat flex", "passive heat", "passive flex"]),
        new("heat_release_louver", "Heat Release Louver", "Standardized Heat Release Register", IndoorModern(), false,
            "Parts Price Book", ["heat release louver", "heat release", "release louver"]),
        new("air_intake_louver", "Air Intake Louver", "Standardized Air Intake Register", IndoorModern(), false,
            "Parts Price Book", ["air intake louver", "air intake", "intake louver"]),
        new("power_vent", "Power Vent", "Run Up to 8 90-Degree Elbows and 100' of Venting",
            Types(FireplaceType.Indoor, FireplaceType.IndoorSeeThrough, FireplaceType.IndoorOutdoorSeeThrough,
                  FireplaceType.Traditional),
            false, "Parts Price Book", ["power vent", "power-vent", "pwr vent", "pwrvent"]),
        new("reflective_black_interior", "Reflective Black Interior",
            "Black reflective interior panels for the back and sides.", Types(FireplaceType.Traditional), true,
            "Indoor Price Book",
            ["reflective black interior", "reflective interior", "black reflective interior", "rb-tra", "rbtra"]),
        new("offset_red_brick_traditional", "Offset Red Brick Traditional", "Offset Red Brick Traditional",
            Types(FireplaceType.Traditional), true, "Indoor Price Book",
            [
                "offset red brick traditional", "red brick traditional", "offset red brick", "red brick", "rd-tra",
                "rdtra"
            ]),
        new("offset_natural_brick_traditional", "Offset Natural Brick Traditional", "Offset Natural Brick Traditional",
            Types(FireplaceType.Traditional), true, "Indoor Price Book",
            [
                "offset natural brick traditional", "natural brick traditional", "offset natural brick",
                "natural brick", "nt-tra", "nttra"
            ]),
        new("offset_black_brick_traditional", "Offset Black Brick Traditional", "Offset Black Brick Traditional",
            Types(FireplaceType.Traditional), true, "Indoor Price Book",
            [
                "offset black brick traditional", "black brick traditional", "offset black brick", "black brick",
                "bl-tra", "bltra"
            ]),
        new("herringbone_red_brick_traditional", "Herringbone Red Brick Traditional",
            "Herringbone Red Brick Traditional", Types(FireplaceType.Traditional), true, "Indoor Price Book",
            ["herringbone red brick", "hb-rd-tra", "hbrdtra"]),
        new("herringbone_natural_brick_traditional", "Herringbone Natural Brick Traditional",
            "Herringbone Natural Brick Traditional", Types(FireplaceType.Traditional), true, "Indoor Price Book",
            ["herringbone natural brick", "hb-nt-tra", "hbnttra"]),
        new("herringbone_black_brick_traditional", "Herringbone Black Brick Traditional",
            "Herringbone Black Brick Traditional", Types(FireplaceType.Traditional), true, "Indoor Price Book",
            ["herringbone black brick", "hb-bl-tra", "hbbltra"]),
        new("herringbone_light_stone_brick_traditional", "Herringbone Light Stone Brick Traditional",
            "Herringbone Light Stone Brick Traditional", Types(FireplaceType.Traditional), true, "Indoor Price Book",
            ["herringbone light stone brick", "light stone brick", "hb-ls-tra", "hblstra"]),
        new("safety_screen", "Safety Screen", "Outdoor safety screen.",
            Types(FireplaceType.Outdoor, FireplaceType.OutdoorSeeThrough), true, "Outdoor Price Book",
            ["safety screen", "screen"]),
    ];

    private static FireplaceType[] IndoorModern() => Types(FireplaceType.Indoor, FireplaceType.IndoorSeeThrough,
                                                           FireplaceType.IndoorOutdoorSeeThrough);
    private static FireplaceType[] Types(params FireplaceType[] types) => types;
}
