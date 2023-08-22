# 1.1.2 through 1.1.5
August, 2023

Working to automate the deployment of this application to NuGet.
This is trickier than I expected.
* All files need to go in the `tools/` folder rather than the `lib/` folder.
* Add a `<packageTypes><packageType name="DotnetTool" /></packageTypes>` section to the NuSpec file.
* Add a `DotNetToolSettings.xml` file to the tools folder. 

# 1.1.1
August 18, 2023

* Improvements to reliability for endpoints that lack documentation.
* Fixed issues with handling of string enums.
* Convert all enums to constants and add reference documentation.
* Generate the github workflow YML file for publishing.
* Remove fixed ErrorResult.cs file.

# Version 1.1.0

Set up automated publishing for new releases on NuGet.