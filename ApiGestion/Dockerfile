# Run from the repository root: docker build -f ApiGestion/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json ./
COPY ApiGestion/ApiGestion.csproj ApiGestion/
COPY DaoLibrary/DaoLibrary.csproj DaoLibrary/
COPY ServiceLibrary/ServiceLibrary.csproj ServiceLibrary/
COPY EntityLibrary/EntityLibrary.csproj EntityLibrary/
RUN dotnet restore ApiGestion/ApiGestion.csproj
COPY ApiGestion/ ApiGestion/
COPY DaoLibrary/ DaoLibrary/
COPY ServiceLibrary/ ServiceLibrary/
COPY EntityLibrary/ EntityLibrary/
COPY database/migrations/ database/migrations/
# Embedded email asset is referenced by the API project outside its own directory.
COPY frontend-cacc/public/cacc-email-header.jpg frontend-cacc/public/
RUN dotnet publish ApiGestion/ApiGestion.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
# Reports use QuestPDF; supply fonts and font discovery in the Linux runtime.
RUN apt-get update \
    && apt-get install -y --no-install-recommends fontconfig fonts-dejavu-core \
    && rm -rf /var/lib/apt/lists/*
ENV ASPNETCORE_HTTP_PORTS=8080
COPY --from=build /app/publish ./
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "ApiGestion.dll"]
