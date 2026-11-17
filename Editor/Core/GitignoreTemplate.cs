namespace UnityGit
{
    public static class GitignoreTemplate
    {
        // Normalised to "\n" because a checkout with core.autocrlf=true turns the literal's line breaks into "\r\n".
        public static readonly string Text = @"# Unity generated
/[Ll]ibrary/
/[Tt]emp/
/[Oo]bj/
/[Bb]uild/
/[Bb]uilds/
/[Ll]ogs/
/[Uu]ser[Ss]ettings/
/[Mm]emoryCaptures/
/[Rr]ecordings/

# Never ignore asset meta files
!/[Aa]ssets/**/*.meta

# Rider plugin
/[Aa]ssets/Plugins/Editor/JetBrains*

# IDE and generated solution files
.vs/
.idea/
.vscode/
.gradle/
.consulo/
ExportedObj/
*.csproj
*.unityproj
*.sln
*.suo
*.tmp
*.user
*.userprefs
*.pidb
*.booproj
*.svd
*.pdb
*.mdb
*.opendb
*.VC.db
*.pidb.meta
*.pdb.meta
*.mdb.meta

# Crash reports and build outputs
sysinfo.txt
crashlytics-build.properties
*.apk
*.aab
*.unitypackage
*.unitypackage.meta
*.app

# Addressables and Android temp assets
/[Aa]ssets/[Aa]ddressable[Aa]ssets[Dd]ata/*/*.bin*
/[Aa]ssets/[Ss]treamingAssets/aa.meta
/[Aa]ssets/[Ss]treamingAssets/aa/*
".Replace("\r\n", "\n");
    }
}
