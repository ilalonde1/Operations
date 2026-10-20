#nullable enable

using System;
using System.Collections.Generic;
using Kor.Operations.EngineeringTools.Dxf;
using Kor.Operations.EngineeringTools.QuantityTakeoff;


namespace Kor.Operations.EngineeringTools.PdfToSafe
{
    /// <summary>
    /// The numbers that decide what a drawing's linework becomes, as SETTINGS rather than literals.
    /// </summary>
    /// <remarks>
    /// Every defect found reading drawings this session was a convention compiled into a binary. A
    /// metric-only size pattern read one of five KOR jobs. A 2.5 aspect ratio discarded 31168's
    /// commonest parkade column — 54 shapes at exactly 14x36 on one sheet — because that job draws
    /// PC01 at 14" x 36" and the limit was written for squarer columns. A 260-point heading scope fit
    /// one sheet size and silently returned nothing for a larger one.
    ///
    /// The DXF-to-ETABS side already solved this: <see cref="PlanClassificationOptions"/> is
    /// populated from KorStandards through 62 `dxf.*` keys, and a job that draws differently states
    /// so instead of being misread. The PDF intake path had none, so these are its keys, in the same
    /// shape and read through the same plumbing.
    ///
    /// ⚠ EVERY DEFAULT HERE IS THE VALUE THE CODE ALREADY USED. Nothing changes behaviour by being
    /// moved; a job that says nothing gets exactly what it got before. The point is that a job CAN
    /// say something, and that the number is findable when a sheet reads wrong.
    ///
    /// ⚠ These are read with <see cref="RuleSettings.Load"/>, not LoadRequired. A missing rule stops
    /// a DXF-to-ETABS production run by design, because that pipeline's numbers were argued over one
    /// at a time. These have not been, and refusing to read a drawing because nobody has banked
    /// a threshold yet would be a worse failure than using the default.
    /// </remarks>
    public sealed record PdfIntakeOptions(
        double SlabMinDiagonalMm,
        double LineMinLengthMm,
        double ColumnMaxSizeMm,
        double ColumnMinDimMm,
        double ColumnMaxAspect,
        double AgreementToleranceMm,
        double AgreementLabelReachMm,
        double MinWallThicknessMm = PdfIntakeOptions.DefaultMinWallThicknessMm,
        double MaxWallThicknessMm = PdfIntakeOptions.DefaultMaxWallThicknessMm,
        double MinWallLengthMm = PdfIntakeOptions.DefaultMinWallLengthMm,
        double MinWallAspect = PdfIntakeOptions.DefaultMinWallAspect)
    {
        /// <summary>THE WORDS A DRAWING NAMES A LEVEL WITH (step 30): the KorStandards row dxf.level.label-words, else the compiled defaults.</summary>
        public IReadOnlyList<string> LevelLabelWords { get; init; } = ScheduleGridReader.DefaultLevelLabelWords;
        /// <summary>The words a level may be NAMED by alone (step 41): dxf.level.name-words, else the compiled defaults.</summary>
        public IReadOnlyList<string> LevelNameWords { get; init; } = ScheduleGridReader.DefaultLevelNameWords;
        /// <summary>The words in an assembly card's layers that make it structural (step 32): dxf.assembly.structural-words, else the compiled defaults.</summary>
        public IReadOnlyList<string> AssemblyStructuralWords { get; init; } = Intake.AssemblySchedule.StructuralWords;
        /// <summary>The words that make it a partition (step 32): dxf.assembly.partition-words, else the compiled defaults.</summary>
        public IReadOnlyList<string> AssemblyPartitionWords { get; init; } = Intake.AssemblySchedule.PartitionWords;
        /// <summary>The words a tendon's force is written in (step 48): dxf.pdf.force-words, else the compiled defaults.</summary>
        public IReadOnlyList<string> ForceWords { get; init; } = Intake.TendonAnchors.DefaultForceWords;
        /// <summary>The words in an engineer's model file name that mark her PRIMARY model - the whole building's gravity model (step 101): dxf.pdf.yardstick-primary-model-words (migration 095), else the compiled defaults.</summary>
        public IReadOnlyList<string> YardstickPrimaryModelWords { get; init; } = DefaultYardstickPrimaryModelWords;
        /// <summary>The words that mark a model that is NOT the building - secondary elements, a crane beam, a mass or check model (step 101): dxf.pdf.yardstick-secondary-model-words (migration 095), else the compiled defaults.</summary>
        public IReadOnlyList<string> YardstickSecondaryModelWords { get; init; } = DefaultYardstickSecondaryModelWords;
        /// <summary>The words a storey is named with for what it carries (step 102) - her L17 MECH is our L17: dxf.pdf.yardstick-storey-qualifier-words (migration 095), else the compiled defaults.</summary>
        public IReadOnlyList<string> YardstickStoreyQualifierWords { get; init; } = Dxf.ModelYardstick.DefaultStoreyQualifierWords;
        /// <summary>The words inside a big X that make it a void (step 106): OPEN TO BELOW on 31202's L6-L13 - dxf.pdf.void-words (migration 097), else the compiled defaults.</summary>
        public IReadOnlyList<string> VoidWords { get; init; } = DefaultVoidWords;
        /// <summary>The words inside a big X that make it a slab, whatever else it says (step 106): 9" SLAB on 31202's ROOF - dxf.pdf.slab-words (migration 097), else the compiled defaults.</summary>
        public IReadOnlyList<string> SlabWords { get; init; } = DefaultSlabWords;
        /// <summary>What an engineer's markup colour declares a member to be, as RRGGBB=TYPE (step 152): burgundy is KOR's
        /// structure ink - dxf.pdf.markup-structure-colours, else the compiled default. Markup only: page linework of the
        /// same colour is the drawing's, not a declaration.</summary>
        public IReadOnlyList<string> MarkupStructureColours { get; init; } = DefaultMarkupStructureColours;
        /// <summary>KOR's structure ink (step 152): #800000, the burgundy every engineer marks walls and columns in (143 E 3rd's
        /// A105: nine wall regions and ten columns, all #800000; its floor measurement #004000, its dimensions #F00000). WALL,
        /// so a region reaches the composer as a wall outline and a column-sized one becomes a column by the composer's rule.</summary>
        /// <para>FFFF00=WALL? - ASSUMED (the trailing ?), 2026-10-19: 143 E 3rd's L1 draws yellow bands round its perimeter and
        /// down its core, no floor; rendered and looked at, they are the ground floor's walls. Nothing states it - 205 of the
        /// engineer's marked-up files hold 87 yellow shapes, 85 of them highlight boxes - so it is written on an -ASSUMED
        /// layer and the report says so.</para>
        /// <para>0080C0=BEAM? - ASSUMED, 2026-10-19: 143 E 3rd draws three blue-filled, red-dashed bands with a width dimension
        /// on them - two 8'-0" across L2 (one with three columns standing in it), one 4'-0" between two columns on L36. Read
        /// as slab edges they were cut as holes (387 and 267 sq ft on L2, 117 on L36). Looked at, they are beam bands. Beams
        /// are not modelled (LayerLedger: a depth would be invented), so the declaration keeps them out of the slab and on a
        /// layer that says BEAM and ASSUMED. 205 of the engineer's marked-up files hold no blue fill: this job's own ink.</para>
        /// <para>FFFF80=BEAM? - ASSUMED, 2026-10-19: the pale yellow, four shapes on 143 E 3rd's L36, each where a yellow wall
        /// line crosses the corridor - the line carried over the doorway, a header, not a hole (they were cut as two).</para>
        /// <para>FFFF00=WALL-BELOW? (step 165, 2026-10-19): the yellow is structure arriving from the storey below and stopping
        /// at this level, not standing on it. Looked at sheet by sheet: L36's yellow columns sit on the typical floor's and
        /// nothing is over them (L37 and the roof step back 1.5 m); L36's burgundy columns are exactly the six that continue
        /// to the roof; the roof's yellow are those same double-height columns arriving; L1's yellow perimeter is the parkade
        /// wall, its burgundy spine the wall going up. Read as WALL and raised by the member-rises rule, they stood four columns
        /// on L37 with nothing to carry and doubled L1's wall.</para>
        public static readonly IReadOnlyList<string> DefaultMarkupStructureColours = ["800000=WALL", "FFFF00=WALL-BELOW?", "0080C0=BEAM?", "FFFF80=BEAM?"];

        /// <summary>The colour declarations as a lookup the DXF writer reads; an entry that does not parse is skipped.</summary>
        public IReadOnlyDictionary<(byte R, byte G, byte B), string> MarkupElementTypes()
        {
            var map = new Dictionary<(byte R, byte G, byte B), string>();
            foreach (string entry in MarkupStructureColours)
            {
                var parts = entry.Split('=', 2, StringSplitOptions.TrimEntries);
                if (parts.Length != 2 || parts[0].Length != 6 || !int.TryParse(parts[0], System.Globalization.NumberStyles.HexNumber, null, out int rgb)) continue;
                // quantized as the reader quantizes a markup's colour (top four bits a channel): FFFF00 is read as F0F000
                map[((byte)((rgb >> 16) & 0xF0), (byte)((rgb >> 8) & 0xF0), (byte)(rgb & 0xF0))] = parts[1].ToUpperInvariant();
            }
            return map;
        }

        /// <summary>A void's words (step 106): OPEN TO BELOW, OPENING, VOID, ATRIUM. The row dxf.pdf.void-words extends these; whole words, any case.</summary>
        public static readonly IReadOnlyList<string> DefaultVoidWords = ["OPEN", "OPENING", "VOID", "ATRIUM"];
        /// <summary>A slab's words (step 106): SLAB. The row dxf.pdf.slab-words extends these.</summary>
        public static readonly IReadOnlyList<string> DefaultSlabWords = ["SLAB"];
        /// <summary>Her primary model's words (step 101): the whole building's gravity model. The row dxf.pdf.yardstick-primary-model-words extends these. Here, not in the analyzer, because the read cache's sources may not reference the analyzer.</summary>
        public static readonly IReadOnlyList<string> DefaultYardstickPrimaryModelWords = ["FULL", "GRAVITY"];
        /// <summary>The words of a model that is not the building (step 101): 31098's "2NDRY ELEMS.EDB" judged 21 storeys a metre off; 31029's "Crane Beam"; mass, check and preliminary models. The row dxf.pdf.yardstick-secondary-model-words extends these.</summary>
        public static readonly IReadOnlyList<string> DefaultYardstickSecondaryModelWords = ["2NDRY", "SECONDARY", "CRANE", "MASS", "CHECK", "PRELIM", "COPY", "SLS"];
        /// <summary>A storey's height when the drawings state none (step 45): dxf.pdf.assumed-storey-height-mm, else the compiled default; always said in the levels file.</summary>
        public double AssumedStoreyHeightMm { get; init; } = Intake.StoreysFromPlans.DefaultAssumedStoreyHeightMm;
        /// <summary>On a wood plan, the thinnest unfilled line pair that is a concrete wall - a retaining wall (step 67): dxf.pdf.unfilled-wall-min-thickness-mm (migration 091), else 8 in.</summary>
        public double UnfilledWallMinThicknessMm { get; init; } = Intake.WallTypeTagging.DefaultUnfilledWallMinThicknessMm;
        /// <summary>A filled rectangle no schedule declares is a wall pier past this on its long side (step 99, her W1): dxf.pdf.pier-min-long-side-mm (migration 094), else 24 in.</summary>
        public double PierMinLongSideMm { get; init; } = Intake.WallPiers.DefaultPierMinLongSideMm;
        /// <summary>... and at least this many times as long as it is thick: dxf.pdf.pier-min-aspect (migration 094), else 2.</summary>
        public double PierMinAspect { get; init; } = Intake.WallPiers.DefaultPierMinAspect;

        // WP5 (2026-09-11): the readers' compiled conventions become rows, tier one. Three are the DXF
        // side's own rows, read here in the DXF side's unit and converted, because the two sides mean
        // the same thing by them (CompiledDefaultsAreTheBankedRowsTests holds both compiled values equal);
        // two are the PDF side's own (migration 085). EveryReaderConstantIsTriagedTests is the table.
        /// <summary>Two slab-edge chain ends this close are one edge broken by what crossed it: dxf.bridge-tolerance (inches on the row), shared with the DXF side.</summary>
        public double SlabEdgeBridgeMm { get; init; } = GeometryFilterService.DefaultSlabEdgeBridgeMm;
        /// <summary>Smaller than this and a closed ring is a stair, a shaft or a box of notes, not a floor: dxf.min-plate-area (square inches on the row), shared with the DXF side.</summary>
        public double MinSlabAreaMm2 { get; init; } = GeometryFilterService.DefaultMinSlabAreaMm2;
        /// <summary>A footing's dashed side joins across gaps up to this: dxf.dash-join-gap (inches on the row), shared with the DXF side.</summary>
        public double DashGapMm { get; init; } = Intake.FootingOutlines.DefaultDashGapMm;
        /// <summary>The scale a sheet is read at when it states none: dxf.pdf.fallback-scale; KOR's plans are 1/8" = 1'-0" (96).</summary>
        public int FallbackScale { get; init; } = DefaultFallbackScale;
        /// <summary>A level ladder of fewer rows is a caption or a table fragment: dxf.pdf.ladder-min-rows.</summary>
        public int LadderMinRows { get; init; } = Intake.StoreyLadder.DefaultMinRows;

        public const int DefaultFallbackScale = 96;

        // Shared KorStandards defaults, banked 2026-09-08: 4", 60", 48", aspect 2.
        // The DXF compiled maximum is narrower (36"); use the banked 60" here.
        // A WALL IS SIX INCHES OR MORE (step 63, 2026-09-14, migration 090): over 101 engineers' models and 51,127 wall
        // areas, not one is under six inches; a filled band thinner is a stud wall, a curb, a line.
        public const double DefaultMinWallThicknessMm = 152.4;
        public const double DefaultMaxWallThicknessMm = 1524.0;
        public const double DefaultMinWallLengthMm = 1219.2;
        public const double DefaultMinWallAspect = 2.0;
        /// <summary>The defaults, which are what the code did before any of this was settable.</summary>
        public static PdfIntakeOptions Default => new(
            SlabMinDiagonalMm:     PdfToSafeConstants.DefaultSlabMinDiagonalMm,
            LineMinLengthMm:       PdfToSafeConstants.DefaultLineMinLengthMm,
            ColumnMaxSizeMm:       1500.0,
            ColumnMinDimMm:        200.0,
            ColumnMaxAspect:       GeometryFilterService.DefaultMaxColumnAspect,
            AgreementToleranceMm:  25.0,
            AgreementLabelReachMm: PlanAgreesWithItsSchedule.DefaultLabelReachMm);   // 2000 mm since step 39: measured on twelve banked pages, see the constant

        public const string Prefix = "dxf.pdf";

        /// <summary>
        /// A column's SHAPE is one convention, shared with the DXF-to-ETABS side.
        /// </summary>
        /// <remarks>
        /// How slender a column may be does not depend on whether the drawing arrived as PDF or CAD,
        /// and KorStandards already states it: `dxf.max-column-aspect` = 3.0, replay-verified, on the
        /// authority of ETABS/e2k. Giving the PDF path its own `dxf.pdf.column-max-aspect` would be a
        /// second definition of one convention. Adopting 3.0 in place of an invented 3.2 changed one
        /// sheet by two shapes and no coverage anywhere.
        ///
        /// ⛔ AND THE COLUMN SIZE BOUNDS ARE **NOT** SHARED, THOUGH THE SAME KEYS EXIST. This was
        /// tried and measured on 2026-09-02. `dxf.min-column-size` 6in and `dxf.max-column-size`
        /// 132in are the DXF side's PLAUSIBILITY check, because there the LAYER already says whether
        /// a shape is a column: SLABEDG or _COL. PdfToSafe has no layers, so the same numbers are
        /// doing a different job — the size window IS the discriminator, and
        /// <see cref="GeometryFilterService.Classify"/> gives a shape to the column branch or the
        /// slab branch on exactly this test.
        ///
        /// Adopting them raised the ceiling from 1500mm to 3353mm and slabs were swallowed whole:
        ///
        ///     31130 p13   slabs 45 -> 23      31168 p11   slabs 67 -> 47
        ///     31138 p11   slabs 102 -> 58     over-detection 2.6x -> 4.3x
        ///
        /// with coverage IDENTICAL on every sheet — not one additional real column was found. One
        /// rule name, two meanings, because one pipeline has layers and the other does not.
        /// </remarks>
        public const string SharedMaxColumnAspect = "dxf.max-column-aspect";
        public const string SharedMinWallThickness = "dxf.min-wall-thickness";
        public const string SharedMaxWallThickness = "dxf.max-wall-thickness";
        public const string SharedMinWallLength = "dxf.min-wall-length";
        public const string SharedMinWallAspect = "dxf.min-wall-aspect";
        public const string SharedBridgeTolerance = "dxf.bridge-tolerance";
        public const string SharedMinPlateArea = "dxf.min-plate-area";
        public const string SharedDashJoinGap = "dxf.dash-join-gap";

        public static IReadOnlyList<string> SettingKeys { get; } =
        [
            SharedMaxColumnAspect,
            SharedMinWallThickness,
            SharedMaxWallThickness,
            SharedMinWallLength,
            SharedMinWallAspect,
            $"{Prefix}.column-max-size-mm",
            $"{Prefix}.column-min-dim-mm",
            $"{Prefix}.slab-min-diagonal-mm",
            $"{Prefix}.line-min-length-mm",
            $"{Prefix}.agreement-tolerance-mm",
            $"{Prefix}.agreement-label-reach-mm",
            $"{Prefix}.assumed-storey-height-mm",
            $"{Prefix}.unfilled-wall-min-thickness-mm",
            $"{Prefix}.pier-min-long-side-mm",
            $"{Prefix}.pier-min-aspect",
            SharedBridgeTolerance,
            SharedMinPlateArea,
            SharedDashJoinGap,
            $"{Prefix}.fallback-scale",
            $"{Prefix}.ladder-min-rows",
        ];

        /// <summary>
        /// Every rule key the PDF side reads, with the value its compiled default supplies, in the
        /// unit the ROW is banked in: the shared DXF keys in inches (the row's unit), the
        /// <c>dxf.pdf.*</c> keys in millimetres. CompiledDefaultsAreTheBankedRowsTests holds each
        /// against its row, and lists the <c>dxf.pdf.*</c> keys as unbanked until a corpus
        /// measurement banks them.
        /// </summary>
        public static IReadOnlyDictionary<string, double> BuiltInRuleValues()
        {
            var d = Default;
            return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                [SharedMaxColumnAspect]  = d.ColumnMaxAspect,
                [SharedMinWallThickness] = d.MinWallThicknessMm / PrintedLength.MmPerInch,
                [SharedMaxWallThickness] = d.MaxWallThicknessMm / PrintedLength.MmPerInch,
                [SharedMinWallLength]    = d.MinWallLengthMm / PrintedLength.MmPerInch,
                [SharedMinWallAspect]    = d.MinWallAspect,
                [$"{Prefix}.slab-min-diagonal-mm"]     = d.SlabMinDiagonalMm,
                [$"{Prefix}.line-min-length-mm"]       = d.LineMinLengthMm,
                [$"{Prefix}.column-max-size-mm"]       = d.ColumnMaxSizeMm,
                [$"{Prefix}.column-min-dim-mm"]        = d.ColumnMinDimMm,
                [$"{Prefix}.agreement-tolerance-mm"]   = d.AgreementToleranceMm,
                [$"{Prefix}.agreement-label-reach-mm"] = d.AgreementLabelReachMm,
                [$"{Prefix}.assumed-storey-height-mm"] = d.AssumedStoreyHeightMm,
                [$"{Prefix}.unfilled-wall-min-thickness-mm"] = d.UnfilledWallMinThicknessMm,
                [$"{Prefix}.pier-min-long-side-mm"] = d.PierMinLongSideMm,
                [$"{Prefix}.pier-min-aspect"] = d.PierMinAspect,
                [SharedBridgeTolerance]  = d.SlabEdgeBridgeMm / PrintedLength.MmPerInch,
                [SharedMinPlateArea]     = d.MinSlabAreaMm2 / (PrintedLength.MmPerInch * PrintedLength.MmPerInch),
                [SharedDashJoinGap]      = d.DashGapMm / PrintedLength.MmPerInch,
                [$"{Prefix}.fallback-scale"]   = d.FallbackScale,
                [$"{Prefix}.ladder-min-rows"]  = d.LadderMinRows,
            };
        }

        /// <summary>Overlay whatever KorStandards states; anything unbanked keeps its default.</summary>
        public static PdfIntakeOptions ApplyRules(
            PdfIntakeOptions options,
            IReadOnlyDictionary<string, RuleSetting> settings)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(settings);

            double WallMm(string key, double fallbackMm)
                => settings.TryGetValue(key, out var setting) ? setting.Value * 25.4 : fallbackMm;

            return options with
            {
                SlabMinDiagonalMm     = settings.ValueOr($"{Prefix}.slab-min-diagonal-mm", options.SlabMinDiagonalMm),
                LineMinLengthMm       = settings.ValueOr($"{Prefix}.line-min-length-mm", options.LineMinLengthMm),
                ColumnMaxSizeMm       = settings.ValueOr($"{Prefix}.column-max-size-mm", options.ColumnMaxSizeMm),
                ColumnMinDimMm        = settings.ValueOr($"{Prefix}.column-min-dim-mm", options.ColumnMinDimMm),
                // shape is shared; the size window is not — see the remarks on SharedMaxColumnAspect
                ColumnMaxAspect       = settings.ValueOr(SharedMaxColumnAspect, options.ColumnMaxAspect),
                MinWallThicknessMm    = WallMm(SharedMinWallThickness, options.MinWallThicknessMm),
                MaxWallThicknessMm    = WallMm(SharedMaxWallThickness, options.MaxWallThicknessMm),
                MinWallLengthMm       = WallMm(SharedMinWallLength, options.MinWallLengthMm),
                MinWallAspect         = settings.ValueOr(SharedMinWallAspect, options.MinWallAspect),
                AgreementToleranceMm  = settings.ValueOr($"{Prefix}.agreement-tolerance-mm", options.AgreementToleranceMm),
                AgreementLabelReachMm = settings.ValueOr($"{Prefix}.agreement-label-reach-mm", options.AgreementLabelReachMm),
                AssumedStoreyHeightMm = settings.ValueOr($"{Prefix}.assumed-storey-height-mm", options.AssumedStoreyHeightMm),
                UnfilledWallMinThicknessMm = settings.ValueOr($"{Prefix}.unfilled-wall-min-thickness-mm", options.UnfilledWallMinThicknessMm),
                PierMinLongSideMm = settings.ValueOr($"{Prefix}.pier-min-long-side-mm", options.PierMinLongSideMm),
                PierMinAspect = settings.ValueOr($"{Prefix}.pier-min-aspect", options.PierMinAspect),
                SlabEdgeBridgeMm      = WallMm(SharedBridgeTolerance, options.SlabEdgeBridgeMm),
                MinSlabAreaMm2        = settings.TryGetValue(SharedMinPlateArea, out var plate) ? plate.Value * 25.4 * 25.4 : options.MinSlabAreaMm2,
                DashGapMm             = WallMm(SharedDashJoinGap, options.DashGapMm),
                FallbackScale         = (int)Math.Round(settings.ValueOr($"{Prefix}.fallback-scale", options.FallbackScale)),
                LadderMinRows         = (int)Math.Round(settings.ValueOr($"{Prefix}.ladder-min-rows", options.LadderMinRows)),
                // the vocabularies (steps 30, 32, 41): a row EXTENDS the compiled defaults, it does not replace them —
                // a practice's phrase is added to what is true of drawings generally, never in place of it
                LevelLabelWords         = Extended("dxf.level.label-words", options.LevelLabelWords),
                LevelNameWords          = Extended("dxf.level.name-words", options.LevelNameWords),
                AssemblyStructuralWords = Extended("dxf.assembly.structural-words", options.AssemblyStructuralWords),
                AssemblyPartitionWords  = Extended("dxf.assembly.partition-words", options.AssemblyPartitionWords),
                ForceWords              = Extended($"{Prefix}.force-words", options.ForceWords),
                YardstickPrimaryModelWords   = Extended($"{Prefix}.yardstick-primary-model-words", options.YardstickPrimaryModelWords),
                YardstickSecondaryModelWords = Extended($"{Prefix}.yardstick-secondary-model-words", options.YardstickSecondaryModelWords),
                YardstickStoreyQualifierWords = Extended($"{Prefix}.yardstick-storey-qualifier-words", options.YardstickStoreyQualifierWords),
                VoidWords = Extended($"{Prefix}.void-words", options.VoidWords),
                SlabWords = Extended($"{Prefix}.slab-words", options.SlabWords),
                MarkupStructureColours = Extended($"{Prefix}.markup-structure-colours", options.MarkupStructureColours),
            };

            IReadOnlyList<string> Extended(string key, IReadOnlyList<string> defaults)
            {
                var banked = settings.ListOr(key, null);
                return banked is null ? defaults : defaults.Concat(banked).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }
        }

        /// <summary>
        /// The options a job states, or the defaults when no connection is given or it cannot be read.
        /// </summary>
        /// <remarks>
        /// Reading drawings is not a production publish, so an unreachable KorStandards degrades to
        /// the defaults rather than refusing. The caller is told which happened.
        /// </remarks>
        public static (PdfIntakeOptions Options, string Source) For(string? connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                return (Default, "defaults (no --rules-db given)");

            try
            {
                return (ApplyRules(Default, RuleSettings.Load(connectionString)), "KorStandards");
            }
            catch (Exception ex)
            {
                return (Default, $"defaults (KorStandards unreadable: {ex.GetType().Name})");
            }
        }
    }
}
