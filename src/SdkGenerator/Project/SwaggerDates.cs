using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace SdkGenerator.Project;

public class SwaggerDate
{
    public string Version { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
}

/// <summary>
/// This tragic object is necessary because swagger files do not contain "dates when this was updated"
/// </summary>
public class SwaggerDates
{
    public List<SwaggerDate> Dates { get; set; } = new();

    public DateOnly GetDateForVersion(string version)
    {
        var date = Dates.Where(d => d.Version == version).FirstOrDefault();
        if (date == null)
        {
            date = new SwaggerDate()
            {
                Version = version,
                Date = DateOnly.FromDateTime(DateTime.UtcNow)
            };
            Dates.Add(date);
        }
        return date.Date;
    }
    
    public static async Task<SwaggerDates> FromDatesFile(string? datesFile)
    {
        var dates = new SwaggerDates();
        if (datesFile != null)
        {
            if (File.Exists(datesFile))
            {
                var text = await File.ReadAllTextAsync(datesFile);
                dates = JsonConvert.DeserializeObject<SwaggerDates>(text) ?? new();
            }
        }

        return dates;
    }

    public async Task SaveDatesFile(string? datesFile)
    {
        if (datesFile != null)
        {
            var text = JsonConvert.SerializeObject(this, Formatting.Indented);
            await File.WriteAllTextAsync(datesFile, text);
        }
    }
}