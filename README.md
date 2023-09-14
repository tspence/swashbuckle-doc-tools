[![NuGet](https://img.shields.io/nuget/v/SdkGenerator.svg?style=plastic)](https://www.nuget.org/packages/SdkGenerator/)

# Swashbuckle SDK Generator

This program allows you to generate a hand-optimized software development kit for different programming languages for 
your REST API.  

This opinionated software makes assumptions about your API and attempts to create a SDK that matches good practices in 
each programming language.  The OpenAPI / Swagger spec permits lots of different ways of doing things; this tool is 
intended to work only with commonly seen use cases.

## Using this program

Here's how to use this program.

1. Install the program using NuGet
```shell
> dotnet tool install --global SdkGenerator
```

2. Create a project file, then fill out all the values you want to use in it
```shell
> sdkgenerator create -p .\myapi.json
```

3. Run the program and build a single language OR build all languages
```shell
> sdkgenerator build -p .\myapi.json
```

You can automate these steps in a Github workflow to execute this program automatically on new releases.

## Supported Languages

| Language   | Supported   | Github Workflows | Notes                 |
|------------|-------------|------------------|-----------------------|
| C#         | Yes         | Automated        | Most complete support |
| Dart       | In Progress | No               | In development        |
| Java       | Yes         | No               |                       |
| Python     | Yes         | No               |                       |
| Ruby       | In Progress | No               | Somewhat supported    |
| TypeScript | Yes         | No               |                       |

## Supported Tools

| Language | Supported | Notes                                                      |
|----------|-----------|------------------------------------------------------------|
| Readme   | Yes       | Markdown-formatted documentation can upload to Guide pages |
| Workato  | Partially | Somewhat supported                                         |

## OpenAPI assumptions

Examples of assumptions about OpenAPI made by this program:
* Only supports OpenAPI 3.0
* Your server supports GZIP encoding and HTTPS connection pooling
* An endpoint returns only a single data type and a single error type
* Each API has a single-word category, a four-word title, and a long remarks section that is a description
* You have a list of public environments (e.g. production, sandbox) that are documented in the SDK
* For test environments or dedicated servers, an SDK user must define a custom environment URL
* [Enums are sometimes unsafe for SDK usage](https://medium.com/codex/should-your-api-use-enums-340a6b51d6c3); all enums are converted to integers or strings
* Nobody intentionally adds HttpStatusCode to their swagger file; if it appears, ignore it
* Each API has a unique `summary` value in the swagger file which will be used as method names for the SDK

## Attribution

[Puzzle icons created by Freepik - Flaticon](https://www.flaticon.com/free-icons/puzzle)