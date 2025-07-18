$backendPath = "./KaizokuBackend"
$project = "KaizokuBackend.csproj"

Push-Location $backendPath
dotnet restore $project
dotnet publish $project -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugSymbols=false  -o bin/linux/amd64
dotnet publish $project -c Release -r linux-arm64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugSymbols=false  -o bin/linux/arm64
docker buildx build -t maxpiva/kaizoku-net:latest --platform linux/amd64,linux/arm64 . --push
Pop-Location

