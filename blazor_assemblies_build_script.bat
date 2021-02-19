REM Rebuild System.Text.Json ref project.
dotnet restore D:\Users\tagoo\Source\repos\runtimelab\src\libraries\System.Text.Json\ref\System.Text.Json.csproj
dotnet msbuild D:\Users\tagoo\Source\repos\runtimelab\src\libraries\System.Text.Json\ref\System.Text.Json.csproj /t:Rebuild /p:TargetFramework=netcoreapp3.0 /p:Configuration=Release

REM Rebuild System.Text.Json src project.
dotnet restore D:\Users\tagoo\Source\repos\runtimelab\src\libraries\System.Text.Json\src\System.Text.Json.csproj
dotnet msbuild D:\Users\tagoo\Source\repos\runtimelab\src\libraries\System.Text.Json\src\System.Text.Json.csproj /t:Rebuild /p:TargetFramework=netcoreapp3.0 /p:Configuration=Release

REM Copy System.Text.Json.dll (implementation dll) to .nuget package cache.
robocopy D:\Users\tagoo\Source\repos\runtimelab\artifacts\bin\System.Text.Json\netcoreapp3.0-Release\ C:\Users\tagoo\.nuget\packages\system.text.json\6.0.0-preview.2.21114.2\lib\netcoreapp3.0\ System.Text.Json.dll /is /it
robocopy D:\Users\tagoo\Source\repos\runtimelab\artifacts\bin\System.Text.Json\netcoreapp3.0-Release\ C:\Users\tagoo\.nuget\packages\system.text.json\6.0.1-dev\lib\netcoreapp3.0\ System.Text.Json.dll /is /it
robocopy D:\Users\tagoo\Source\repos\runtimelab\artifacts\bin\System.Text.Json\netcoreapp3.0-Release\ C:\Users\tagoo\.nuget\packages\microsoft.netcore.app.runtime.browser-wasm\6.0.0-preview.2.21114.2\runtimes\browser-wasm\lib\net6.0\ System.Text.Json.dll /is /it

REM ===

REM Rebuild System.Net.Http.Json ref project.
dotnet restore D:\Users\tagoo\Source\repos\runtimelab\src\libraries\System.Net.Http.Json\ref\System.Net.Http.Json.csproj
dotnet msbuild D:\Users\tagoo\Source\repos\runtimelab\src\libraries\System.Net.Http.Json\ref\System.Net.Http.Json.csproj /t:Rebuild /p:TargetFramework=net6.0 /p:Configuration=Release

REM Rebuild System.Net.Http.Json src project.
dotnet restore D:\Users\tagoo\Source\repos\runtimelab\src\libraries\System.Net.Http.Json\src\System.Net.Http.Json.csproj
dotnet msbuild D:\Users\tagoo\Source\repos\runtimelab\src\libraries\System.Net.Http.Json\src\System.Net.Http.Json.csproj /t:Rebuild /p:TargetFramework=net6.0 /p:Configuration=Release

REM Copy System.Net.Http.Json.dll (implementation dll) to .nuget package cache.
robocopy D:\Users\tagoo\Source\repos\runtimelab\artifacts\bin\System.Net.Http.Json\net6.0-Release\ C:\Users\tagoo\.nuget\packages\system.net.http.json\6.0.0-preview.2.21114.2\lib\net5.0\ System.Net.Http.Json.dll /is /it
robocopy D:\Users\tagoo\Source\repos\runtimelab\artifacts\bin\System.Net.Http.Json\net6.0-Release\ C:\Users\tagoo\.nuget\packages\microsoft.netcore.app.runtime.browser-wasm\6.0.0-preview.2.21114.2\runtimes\browser-wasm\lib\net6.0\ System.Net.Http.Json.dll /is /it
robocopy D:\Users\tagoo\Source\repos\runtimelab\artifacts\bin\System.Net.Http\net6.0-windows-Release\ C:\Users\tagoo\.nuget\packages\microsoft.netcore.app.runtime.browser-wasm\6.0.0-preview.2.21114.2\runtimes\browser-wasm\lib\net6.0\ System.Net.Http.dll /is /it
REM dll location: D:\Users\tagoo\Source\repos\runtimelab\artifacts\bin\System.Net.Http.Json\net6.0-Release\System.Net.Http.Json.dll

REM ===

REM Rebuild System.Text.Json.SourceGeneration project.
dotnet msbuild D:\Users\tagoo\Source\repos\runtimelab\src\libraries\System.Text.Json\System.Text.Json.SourceGeneration\System.Text.Json.SourceGeneration.csproj /p:BuildTargetFramework=netstandard2.0 /p:Configuration=Debug

REM Copy System.Text.Json.SourceGeneration.dll to .nuget package cache.
robocopy D:\Users\tagoo\Source\repos\runtimelab\artifacts\bin\System.Text.Json.SourceGeneration\netstandard2.0-Debug\ C:\Users\tagoo\.nuget\packages\system.text.json\6.0.0-preview.2.21105.12\analyzers\dotnet\cs System.Text.Json.SourceGeneration.dll /is /it
robocopy D:\Users\tagoo\Source\repos\runtimelab\artifacts\bin\System.Text.Json.SourceGeneration\netstandard2.0-Debug\ C:\Users\tagoo\.nuget\packages\system.text.json\6.0.0-preview.2.21114.2\analyzers\dotnet\cs System.Text.Json.SourceGeneration.dll /is /it
robocopy D:\Users\tagoo\Source\repos\runtimelab\artifacts\bin\System.Text.Json.SourceGeneration\netstandard2.0-Debug\ C:\Users\tagoo\.nuget\packages\system.text.json\6.0.1-dev\analyzers\dotnet\cs System.Text.Json.SourceGeneration.dll /is /it
robocopy D:\Users\tagoo\Source\repos\runtimelab\artifacts\bin\System.Text.Json.SourceGeneration\netstandard2.0-Debug\ C:\Users\tagoo\.nuget\packages\microsoft.netcore.app.runtime.browser-wasm\6.0.0-preview.2.21114.2\analyzers\dotnet\cs System.Text.Json.SourceGeneration.dll /is /it

REM ===

REM Rebuild Microsoft.JSInterop
dotnet build D:\Users\tagoo\Source\repos\aspnetcore\src\JSInterop\Microsoft.JSInterop\src\Microsoft.JSInterop.csproj /p:DisablePackageReferenceRestrictions=true /bl:check.binlog -c Release /p:TargetFramework=net6.0
robocopy D:\Users\tagoo\Source\repos\aspnetcore\artifacts\bin\Microsoft.JSInterop\Release\net6.0 C:\Users\tagoo\.nuget\packages\microsoft.jsinterop\6.0.0-preview.2.21114.2\lib\net6.0 Microsoft.JSInterop.dll /is /it

REM Rebuild Microsoft.JSInterop.WebAssembly
dotnet build D:\Users\tagoo\Source\repos\aspnetcore\src\Components\WebAssembly\JSInterop\src\Microsoft.JSInterop.WebAssembly.csproj /p:DisablePackageReferenceRestrictions=true /bl:check.binlog -c Release /p:TargetFramework=net6.0
robocopy D:\Users\tagoo\Source\repos\aspnetcore\artifacts\bin\Microsoft.JSInterop.WebAssembly\Release\net6.0 C:\Users\tagoo\.nuget\packages\microsoft.jsinterop.webassembly\6.0.0-preview.2.21114.2\lib\net6.0 Microsoft.JSInterop.WebAssembly.dll /is /it

REM Rebuild Microsoft.AspNetCore.Components.Web
dotnet build D:\Users\tagoo\Source\repos\aspnetcore\src\Components\Web\src\Microsoft.AspNetCore.Components.Web.csproj /p:DisablePackageReferenceRestrictions=true /bl:check.binlog -c Release /p:TargetFramework=net6.0
robocopy D:\Users\tagoo\Source\repos\aspnetcore\artifacts\bin\Microsoft.AspNetCore.Components.Web\Release\net6.0 C:\Users\tagoo\.nuget\packages\microsoft.aspnetcore.components.web\6.0.0-preview.2.21114.2\lib\net6.0 Microsoft.AspNetCore.Components.Web.dll /is /it
