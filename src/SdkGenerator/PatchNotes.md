# 1.1.7
September 10, 2023

* Updated build process to modern DotNet standards, with thanks to [Chet Husk](https://github.com/baronfel)
* We no longer need a DotNetToolSettings.xml file nor a NuSpec file

# 1.1.2 through 1.1.6
August, 2023

Working to automate the deployment of this application to NuGet.
This is trickier than I expected.
* All files need to go in the `tools/` folder rather than the `lib/` folder.
* Add a `<packageTypes><packageType name="DotnetTool" /></packageTypes>` section to the NuSpec file.
* Add a `DotNetToolSettings.xml` file to the tools folder. 
* The file must be named `DotnetToolSettings.xml` exactly; any capitalization differences cause it to fail.

# 1.1.1
August 18, 2023

* Improvements to reliability for endpoints that lack documentation.
* Fixed issues with handling of string enums.
* Convert all enums to constants and add reference documentation.
* Generate the github workflow YML file for publishing.
* Remove fixed ErrorResult.cs file.

# Version 1.1.0

Set up automated publishing for new releases on NuGet.