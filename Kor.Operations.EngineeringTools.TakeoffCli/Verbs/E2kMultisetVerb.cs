// The takeoff verb `e2k-multiset`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
// DID A REVISION MOVE A MEMBER, OR ONLY RENAME ONE?
//
// The multiset of (kind, plan position, storey) is what a rename-only change must preserve. Object
// count is expected to fall -- a stack merge takes 1,769 column objects to 268 -- and not one
// member may appear, vanish or move while it does.
//
// Usage: takeoff e2k-multiset <before.e2k> <after.e2k>
internal static class E2kMultisetVerb
{
    public static bool Matches(string[] args) => args.Length >= 1 && args[0].Equals("e2k-multiset", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("Usage: takeoff e2k-multiset <before.e2k> <after.e2k>");
            return 1;
        }

        var multiset = MemberPlanStoreyMultisetPreserved.Compare(args[1], args[2]);
        if (multiset.Preserved)
        {
            Console.WriteLine(
                $"e2k-multiset: every (kind, plan position, storey) assignment in {Path.GetFileName(args[1])} " +
                $"is still in {Path.GetFileName(args[2])}, and nothing was added.");
            return 0;
        }

        Console.Error.WriteLine($"e2k-multiset: {multiset.Message}");
        return 3;
    }
}
