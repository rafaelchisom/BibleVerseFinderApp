# Stage 1: build  
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build  
WORKDIR /src  
  
COPY BibleVerseFinder.sln ./  
COPY BibleVerseFinder/*.csproj ./BibleVerseFinder/  
RUN dotnet restore  
  
COPY BibleVerseFinder/. ./BibleVerseFinder/  
WORKDIR /src/BibleVerseFinder  
RUN dotnet publish -c Release -o /app/publish  
  
# Stage 2: runtime  
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime  
WORKDIR /app  
ENV ASPNETCORE_URLS=http://+:${PORT:-5000}  
ENV ASPNETCORE_ENVIRONMENT=Production  
  
COPY --from=build /app/publish .  
EXPOSE 5000  
  
ENTRYPOINT ["dotnet", "BibleVerseFinder.dll"]  