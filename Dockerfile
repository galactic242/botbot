# build stage
FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /app

# copy solution & project first
COPY *.sln ./
COPY *.csproj ./

# restore packages with verbose logs
RUN dotnet restore -v diag

# copy everything else
COPY . ./

# publish with verbose logs to catch errors
RUN dotnet publish MoonsecDeobfuscator.csproj -c Release -o out -v diag

# runtime stage
FROM mcr.microsoft.com/dotnet/runtime:7.0
WORKDIR /app
COPY --from=build /app/out ./
CMD ["dotnet", "MoonsecDeobfuscator.dll"]
