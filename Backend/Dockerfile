FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["SyntInfo.Api/SyntInfo.Api.csproj", "SyntInfo.Api/"]
COPY ["SyntInfo.Application/SyntInfo.Application.csproj", "SyntInfo.Application/"]
COPY ["SyntInfo.Domain/SyntInfo.Domain.csproj", "SyntInfo.Domain/"]
COPY ["SyntInfo.Infrastructure/SyntInfo.Infrastructure.csproj", "SyntInfo.Infrastructure/"]

RUN dotnet restore "SyntInfo.Api/SyntInfo.Api.csproj"

COPY . .
WORKDIR "/src/SyntInfo.Api"
RUN dotnet build "SyntInfo.Api.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "SyntInfo.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Set environment variables
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "SyntInfo.Api.dll"]
