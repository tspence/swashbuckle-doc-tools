using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using SdkGenerator.Project;
using SdkGenerator.Schema;

namespace SdkGenerator.Languages;

public class DartSdk : ILanguageSdk
{   
    public async Task Export(GeneratorContext context)
    {
        if (context.Project.Dart == null)
        {
            return;
        }

        await ScribanFunctions.ExecuteTemplate(context, 
            Path.Combine(".", "templates", "dart", "ApiClient.dart.scriban"),
            Path.Combine(context.Project.Dart.Folder, context.Project.Dart.ClassName + ".dart"));
    }
    
    public string LanguageName()
    {
        return "Dart";
    }
}