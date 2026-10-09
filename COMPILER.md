# Compiler Relais — le guide

Relais est écrit en C# (Windows Forms, .NET Framework 4.8). Le compilateur est **déjà fourni avec Windows** :
tu n'as rien à installer pour la méthode 1.

---

## Méthode 1 — La plus simple : `compiler.bat` (2 secondes)

1. Ouvre le dossier `sources`.
2. Double-clique sur **`compiler.bat`**.
3. `Relais.exe` est (re)créé dans le dossier parent.

Idéal pour tester une modification du code. Le compilateur de Windows ne gère que le C# 5 :
garde le style du code actuel (pas de `$"..."`, pas de `?.`).

---

## Méthode 2 — La meilleure : un dépôt GitHub (compilation + mises à jour automatiques)

Avec cette méthode, GitHub compile Relais sur un PC Windows propre à chaque modification,
fabrique l'installateur, et publie les versions. Relais se met ensuite à jour **tout seul** chez toi et chez tes amis.

### Une seule fois

1. Crée un compte sur https://github.com (gratuit).
2. Clique **New repository** → nom : `relais` → **Public** (obligatoire pour que les mises à jour
   automatiques puissent lire les versions sans mot de passe) → **Create repository**.
3. Envoie le contenu du dossier `sources` dans le dépôt, le plus simple étant
   **GitHub Desktop** (https://desktop.github.com) :
   *File > Add local repository* → choisis le dossier `sources` → *Publish repository*.
   Vérifie que le dossier caché `.github` est bien parti (c'est lui qui contient la recette de compilation).
4. Dans Relais : **Options > Mises à jour > Dépôt GitHub** → écris `tonpseudo/relais`.

### À chaque nouvelle version (tout depuis GitHub Desktop)

1. Remplace les fichiers modifiés dans ton dossier, puis dans GitHub Desktop : écris un résumé en bas à gauche,
   **Commit to main**, puis **Push origin**.
2. Onglet **History** (en haut à gauche) → **clic droit sur le commit le plus haut** → **Create Tag…**
   → tape `v2.0.2` → **Create Tag**.
3. Clique sur **Push origin** (en haut) : le tag part sur GitHub.
4. 3–4 minutes plus tard, la Release `v2.0.2` existe avec `Relais.exe` et `Relais-Setup.exe`
   (pour suivre : *Repository > View on GitHub* puis onglet **Actions**).
   Tous les Relais configurés avec ton dépôt proposeront la mise à jour.

Alternative sur le site : **Releases > Draft a new release** → *Choose a tag* → `v2.0.2` → **Publish release**.

Le numéro de version du programme est pris automatiquement dans le nom du tag (`v2.0.1` → 2.0.1.0).

---

## Méthode 3 — Visual Studio (pour développer confortablement)

1. Installe **Visual Studio Community** (gratuit), charge de travail *Développement .NET Desktop*.
2. *Créer un projet* → **Application Windows Forms (.NET Framework)**, framework **4.8**.
3. Supprime `Form1.cs` et `Program.cs`, puis *Ajouter > Élément existant* → tous les `.cs` du dossier `sources`.
4. *Références* → ajoute **System.Web.Extensions**.
5. *Propriétés du projet > Application > Icône* → `relais.ico`.
6. **F5** pour lancer avec le débogueur.

---

## L'installateur

`installer/relais.iss` (Inno Setup, gratuit : https://jrsoftware.org) installe Relais dans
`%LOCALAPPDATA%\Programs\Relais`, **sans droits administrateur**, avec raccourcis et désinstallation.
Le workflow GitHub le construit tout seul ; en local : compile d'abord `build\Relais.exe`, puis ouvre le `.iss`
dans Inno Setup et clique *Compile*.

---

## Antivirus / « Windows a protégé votre ordinateur »

Relais écoute le clavier pour tes raccourcis : certains antivirus se méfient des programmes non signés qui font ça.
Trois actions, de la plus rapide à la plus efficace :

1. **Signaler le faux positif à Microsoft (gratuit, 5 min par version)**
   - Va sur https://www.microsoft.com/wdsi/filesubmission → « Software developer ».
   - Envoie `Relais.exe` (et `Relais-Setup.exe`), choisis « Incorrectly detected as malware/malicious ».
   - Explique : « Organizer open source pour Dofus, code public sur github.com/Vior-g/relais ».
   - Microsoft répond en général sous quelques jours ; Defender cesse alors de bloquer cette version.
   - Même chose possible chez ton antivirus (Avast, Kaspersky, Norton… ont tous un formulaire « faux positif »).
2. **Toujours publier via GitHub Actions** : l'exe est compilé à partir du code public, ce qui rassure
   les joueurs (et les antivirus qui regardent la réputation du fichier).
3. **Signer l'exe (supprime l'écran SmartScreen)**
   - Gratuit pour les projets open source : **SignPath Foundation** (https://signpath.org).
   - Conditions : dépôt public, licence open source (ajoute un fichier `LICENSE`, par ex. MIT),
     builds faits par GitHub Actions, et une petite page qui présente le projet (la page GitHub Pages ci-dessous).
   - Une fois accepté, SignPath te donne une étape à ajouter au workflow : envoie-moi leurs instructions et je l'intègre.

## Page de présentation (GitHub Pages)

Le dossier `docs/` contient le site de Relais (une seule page + captures d'écran).

1. Copie le dossier `docs` à la racine de ton dépôt, commit, push.
2. Sur github.com : ton dépôt > **Settings > Pages** > Source « Deploy from a branch » >
   Branch `main`, dossier `/docs` > Save.
3. Une minute plus tard, le site est en ligne sur **https://vior-g.github.io/relais/**
   (le bouton « Page de Relais » dans Options y mène).

## Bouton « Soutenir »

1. Crée une page de dons (au choix) : **Ko-fi** (ko-fi.com, gratuit, 0 % de commission sur les dons),
   **Tipeee** (tipeee.com, très utilisé en France) ou **GitHub Sponsors** (github.com/sponsors).
2. Mets l'adresse à deux endroits :
   - `Links.cs` : `public const string Support = "https://ko-fi.com/tonpseudo";`
   - `docs/index.html` : tout en bas, `var SUPPORT_URL = "https://ko-fi.com/tonpseudo";`
3. Publie une nouvelle version : le bouton « Soutenir Relais » (Options et palette) ouvrira ta page.
   Tant que l'adresse est vide, il ouvre la page GitHub du projet (pour une étoile).
