#nullable enable
using Kor.Operations.Core.Models.Brochure;

namespace Kor.Operations.Core.Services
{
    public interface IBrochureContactStore
    {
        BrochureContactConfig Load();

        void Save(BrochureContactConfig config);
    }
}
