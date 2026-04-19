# Fog Switcher

`Fog Switcher` est un prototype de server selector pour Dead by Daylight, inspire par `Make Your Choice` mais reconstruit avec une interface originale et un scope volontairement simple.

## Ce que fait l'app

- affiche les regions DbD principales
- mesure la latence avec un code couleur `vert / jaune / orange / rouge`
- affiche les queues times `killer` et `survivor`
- utilise les endpoints publics de `deadbyqueue.com`
- permet de cocher une ou plusieurs regions a garder ouvertes
- ecrit une section dediee dans le fichier `hosts` Windows pour bloquer les autres regions
- peut retirer uniquement sa propre section sans toucher aux autres lignes du `hosts`

## UI et logique

- `Use Best Ping` coche automatiquement la meilleure region disponible selon la mesure actuelle
- `Apply Selection` peut demander une elevation Windows uniquement au moment d'ecrire dans `C:\Windows\System32\drivers\etc\hosts`
- `Clear Locks` retire le bloc cree par l'application
- l'app reste consultable sans droits admin pour le ping et les queues
- `app_icon.png` est maintenant la source unique des icones Windows, l'`.ico` etant regenere au build

## Build

### Visual Studio

1. Ouvrir [FogSwitcher.sln](./FogSwitcher.sln)
2. Selectionner `Release | x64` si tu veux republier proprement
3. Compiler ou publier le projet `FogSwitcher`

### CLI

Build simple :

```powershell
dotnet build .\src\FogSwitcher\FogSwitcher.csproj
```

Publish `.exe` autonome :

```powershell
dotnet publish .\src\FogSwitcher\FogSwitcher.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:DebugType=None `
  -p:DebugSymbols=false `
  -p:NuGetAudit=false
```

## Mises a jour via GitHub Releases

- Le projet contient maintenant une verification de mise a jour au lancement.
- Pour l'activer, renseigner `GitHubRepository` dans [src/FogSwitcher/UpdateChannel.cs](./src/FogSwitcher/UpdateChannel.cs) au format `owner/repository`.
- Publier ensuite chaque version sur GitHub Releases avec un tag semantique comme `v1.0.1` et joindre au minimum `FogSwitcher.exe`.
- Au lancement, si une release plus recente est detectee, l'app proposera d'ouvrir directement la page ou l'asset de telechargement.

## Sortie de publish

Apres un publish single-file, le binaire se trouve ici :

`src/FogSwitcher/bin/Release/net10.0-windows/win-x64/publish/FogSwitcher.exe`

Ce dossier ne doit pas etre commit sur GitHub.

## Nettoyage avant commit

- Le depot est configure pour ignorer automatiquement `.vs/`, `.dotnet/`, `bin/`, `obj/`, `dist/`, les fichiers `*.user`, `*.suo` et `*.log`.
- Si tu veux aussi nettoyer physiquement le dossier avant un commit ou avant de partager le projet, lance :
- Ferme Visual Studio avant ce nettoyage si tu veux eviter que `.vs/`, `bin/`, `obj/` ou `app.ico` soient recrees pendant l'operation.
- Si tu veux aussi nettoyer physiquement le dossier avant un commit ou avant de partager le projet, lance :

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Clean-Workspace.ps1
```

- Ce script supprime les artefacts IDE/build/publish locaux et laisse uniquement les fichiers utiles du projet.

## Notes

- Le fichier `hosts` est sauvegarde automatiquement en `hosts.bak` avant ecriture.
- Les queues proviennent d'un service tiers : `https://api2.deadbyqueue.com/`.
- L'application n'est pas affiliee a Behaviour Interactive.
