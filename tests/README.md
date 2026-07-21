# Tests d'intégration

Ce projet contient des tests unitaires et des tests d'intégration pour valider le wrapper C# de transcribe.cpp.

## Tests unitaires

Les tests unitaires ne nécessitent pas de bibliothèque native ni de modèle GGUF. Ils valident :
- La génération de code
- La parité des enums
- La structure des types

Exécution :
```bash
dotnet test
```

## Tests d'intégration

Les tests d'intégration nécessitent :
1. La bibliothèque native transcribe.cpp
2. Un modèle GGUF de Whisper

### Configuration

1. Téléchargez la bibliothèque native :
```bash
./fetch-native.sh
```

2. Téléchargez un modèle GGUF (par exemple, le modèle tiny) :
```bash
mkdir -p test-models
curl -L -o test-models/ggml-tiny.bin https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-tiny.bin
```

3. Créez un fichier audio de test :
```bash
mkdir -p test-audio
# Utilisez ffmpeg ou un autre outil pour créer un fichier WAV 16kHz mono
ffmpeg -f lavfi -i "sine=frequency=440:duration=1" -ar 16000 -ac 1 test-audio/test.wav
```

### Exécution

Utilisez le script d'intégration :
```bash
./run-integration-tests.sh
```

Ou exécutez les tests directement :
```bash
export LD_LIBRARY_PATH="$PWD/native-packages/linux-x64/runtimes/linux-x64/native:$LD_LIBRARY_PATH"
dotnet test --filter "FullyQualifiedName~HighLevelApiTests"
```

## Structure des tests

- `EnumParityTest.cs` : Vérifie la parité des enums entre Rust et C#
- `GoldenFileTest.cs` : Vérifie que le code généré correspond au fichier de référence
- `HighLevelApiTests.cs` : Tests d'intégration de l'API haut niveau

## Notes

- Les tests nécessitant la bibliothèque native sont marqués avec `Skip = "Requires native library"`
- Les tests nécessitant un modèle GGUF sont marqués avec `Skip = "Requires integration test environment"`
- Les tests sont automatiquement ignorés si les dépendances ne sont pas présentes
