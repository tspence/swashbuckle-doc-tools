using System.Threading.Tasks;
using SdkGenerator.Project;

namespace SdkGenerator.Languages;

public interface ILanguageSdk
{
    Task Export(GeneratorContext context);
    string LanguageName();
}