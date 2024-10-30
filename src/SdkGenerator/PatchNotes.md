# 1.3.8
October 20, 2024

* Fixed issue where ignored parameters could be null

# 1.3.7
October 20, 2024

* Fixed a minor typo that caused Typescript and Python-only build configs to fail

# 1.3.6
October 18, 2024

* Fixed a minor typo that caused Java-only SDK builds to fail

# 1.3.5
October 4, 2024

* Improvements to Java pom.xml format to deploy via SonaType Central
* Python package names are properly sorted
* Python array results are properly parsed, even if they have extra fields
* Nested references no longer generate incorrect imports in Python

# 1.3.4
September 30, 2024

* Updated C# / DotNet client generation to permit external HttpClient objects

# 1.3.3
September 14, 2024

* Improvements for Dart SDK - maybe not functional for everyone but works for one client

# 1.3.2
September 2, 2024

* Reduced dependencies for C# / DotNet SDK
* Made Environment.MachineName optional
* Modified file upload APIs to take byte arrays rather than filenames
* New option -f for testing - ability to specify a Swagger/OpenAPI file on disk for builds

# 1.3.1
August 18, 2024

* Make automated publishing workflow scripts create-only so updates do not trigger GitHub security

# 1.3.0
August 13, 2024

* New modes to calculate patch notes and release name for use with automated PRs 

# 1.2.6
July 25, 2024

* Fix issue with path combining
* Upgrade to DotNet 8.0
* Do not generate markdown files if not requested
* Make security schemes section blankout optional if users don't want to save keys
* Python: Fix capitalization/list issue
* TypeScript: Fix duplicated import for nested classes

# 1.2.5
March 13, 2024

* Fix issues with embedded resources so this can work from DotNet tool

# 1.2.4
February 10, 2024

* More capitalization improvements - better handle imprecise inputs
* Fixed issues with Python SDK for array uploads
* C# SDK uses URI object instead of string for custom endpoint

# 1.2.3
January 29, 2024

* Allow specific endpoints to be ignored during SDK generation
* Better capitalization for multi-word phrases converted to PascalCase
* Fixed issues with Python SDK generation, json double conversion

# 1.2.2
January 12, 2024

* Numerous small fixes for API endpoints that download octet-streams/byte arrays
* Better logic for excluding endpoints and excluding parameters - patch notes generate correctly
* SDKs now generate endpoints that download blobs and still parse errors correctly
* Treat both "byte" and "byte[]" as blob download endpoints
* Python uses immutable bytes object
* Fixes for readme uploads

# 1.2.1
October 22, 2023

* Class and property generation can now avoid language-specific keywords
* Variable names are now cleansed for parameters (some APIs use $param=value)
* Java now uses semver3 as is becoming the standard in most places
* Generated API documentation will now only link to data model pages if specified
* Readme can now select between `list` and `table` style data model documentation

# 1.2.0
October 11, 2023

* Verified that Python and TypeScript work correctly, at least for my current SDKs

# 1.1.9
October 9, 2023

* Improvements for patch notes generation
* Cleaned up the Python export to use the latest idioms and to work with single-result-object

# 1.1.8
September 14, 2023

* Minor fixes for local debugging and duplicate API names

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