using System.Collections.Generic;
using System.Threading.Tasks;
namespace Kor.Operations.Data
{
    public sealed class DataFacade
    {
        public static DataFacade Instance { get; } = new DataFacade();
        private DataFacade() { }

        public Task<List<(string Name, string Email, bool External)>> SearchContactsAsync(string query)
            => Task.FromResult(new List<(string, string, bool)>());

        public Task<List<(string ProjectNumber, string ProjectName)>> SearchProjectsAsync(string query)
            => Task.FromResult(new List<(string, string)>());
    }
}
