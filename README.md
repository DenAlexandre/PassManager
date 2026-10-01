# PassManager

Gestionnaire de mots de passe multi-plateforme inspiré de KeePass : client .NET MAUI (Android, iOS/macOS, Windows) + backend ASP.NET Core / PostgreSQL dockerisé.

Voir le plan d'architecture complet dans la conversation de conception (schéma BDD, protocole de synchronisation, format du coffre local, modèle de partage).

## Structure

- `backend/src/{Api,Domain,Application,Infrastructure}` — API ASP.NET Core (.NET 10)
- `backend/tests` — tests unitaires et d'intégration
- `src/PassManager.Core` — crypto, format de coffre local, moteur de synchronisation (sans dépendance MAUI)
- `src/PassManager.Maui` — client MAUI (Android/iOS/macOS/Windows)
- `infra/docker-compose.yml` — déploiement des 2 services (`api`, `postgres`), utilisable avec Portainer

## Démarrage backend local

```
cp infra/.env.example infra/.env
# éditer infra/.env (mots de passe, clés)
docker compose --env-file infra/.env -f infra/docker-compose.yml up --build
```

## Build

```
dotnet build PassManager.sln
dotnet test PassManager.sln
```
