# FiveM Vehicle Installer

Aplicativo Windows em C#/.NET 10 e WPF para extrair veículos add-on de GTA V Legacy, preparar resources separados e administrar packs próprios.

## Importar veículos

1. Adicione RPF, ZIP, RAR, 7z ou uma pasta contendo os mods e clique em **Analisar**.
2. Revise os candidatos, os motivos de bloqueio e a verificação de compatibilidade acima da tabela.
3. Marque os pacotes que deseja incluir. Escolha **Separados** ou **Pack unificado**.
4. Configure nomes, sons e destinos e exporte. A atividade mostra os caminhos e as linhas `ensure`.

O programa usa a versão **DLC/RPF** do mod. Versões **FiveM ready** com `fxmanifest.lua` ou `__resource.lua` são ignoradas, inclusive dentro de compactados que também contenham a versão Add-On. Elas podem ser instaladas manualmente. O aplicativo não executa os manifests dessas versões. DLCs já extraídos, identificados por setup2.xml/content.xml e sem manifest FiveM, continuam aceitos para revisão dos arquivos originais do RPF.

Um `vehicles.rpf` isolado normalmente não contém os metadados necessários: selecione o DLC completo. Áudio, scripts, idiomas e dependências não suportadas ficam fora da conversão e são indicados no relatório. RPF criptografado e GTA V Enhanced não são suportados.

## Identificação de autoria

A análise procura atribuições em `.txt`, `.md` e `.markdown`, inclusive READMEs fora do RPF dentro do compactado, e procura `by Nome` no nome do arquivo ou das pastas do pacote. Reconhece, por exemplo, `Author:`, `Autor:`, `Converted by`, `Converted to GTA5 by:`, `Convert / edit .`, `Main Works:` e `Other Work:`, além de títulos iniciais como `1993 Nissan Cefiro A31 by Akkariin`. Versões como `by Navefa 1.2` não fazem parte do nome registrado.

Os detalhes mostram **Autoria encontrada** e a aba **Créditos**, com nome, função, caminho da fonte e trecho original. Nomes vindos de arquivos/pastas aparecem como **Indício no nome**; a função de conversão/edição permanece distinta de autor. A detecção é uma heurística: não comprova autoria, não unifica apelidos automaticamente e pode retornar várias atribuições. Créditos de handling, peças, fotografias e modelos base não são tratados como autoria do mod.

Os créditos ficam por origem em `.fvpack.json` e em `relatorio.txt`, tanto na saída separada quanto no pack. São preservados ao reabrir, atualizar, renomear uma origem ou separar o pack. Índices antigos continuam abrindo, mas não recuperam créditos que nunca foram registrados: reimporte o mod original para procurar suas fontes.

A busca usa os arquivos da entrada selecionada. Ao importar somente um `dlc.rpf`, um README externo não será lido; selecione o compactado ou a pasta completa. Textos compartilhados entre vários DLCs sem associação inequívoca e READMEs de versões FiveM ready são ignorados. URLs, instruções e menções genéricas a terceiros não identificam automaticamente um autor. Sem evidência reconhecida, aparece **Não identificada**. A leitura opcional limita cada texto a 256 KiB e registra até 128 evidências por pacote. Não consulta sites nem altera os mods originais.

## Editar um pack existente

1. Clique em **Abrir pack** e selecione a pasta de um pack criado pelo aplicativo.
2. O índice `.fvpack.json` recupera suas origens e os valores atuais dos metadados. Os RPFs originais não são necessários para manter ou remover carros existentes.
3. Desmarque os pacotes que deseja remover. Para adicionar carros, importe novos RPFs/compactados e clique em **Analisar**.
4. Edite os sons individualmente ou em lote e clique em **Salvar alterações**.

O programa atualiza o mesmo resource, seu manifest e seu índice. Assets e metadados inalterados mantêm seus arquivos; somente inclusões, remoções e mudanças são gravadas. Assets compartilhados são preservados enquanto alguma origem os usar. Renomear uma origem pode exigir reorganizar seus caminhos.

**Salvar como novo** mantém a coleção aberta e permite escolher outro nome/destino. No modo **Separados**, os pacotes podem ser exportados para destinos individuais.

A remoção é por pacote de origem. Um DLC com vários veículos e metadados compartilhados permanece como uma unidade, pois separar itens do XML exigiria reescrever os arquivos. Resources FiveM externos sem índice não são abertos nem separados automaticamente. Packs anteriores do aplicativo com índice versão 1 continuam compatíveis; novas exportações separadas também recebem índice.

A atualização verifica se o pack mudou fora do programa desde sua abertura. Nesse caso, reabra-o antes de salvar. Arquivos auxiliares não gerenciados, como anotações, são preservados; arquivos de veículos fora do índice ou alterações incompatíveis no manifest bloqueiam a abertura. Uma colisão com arquivo externo bloqueia a gravação.

Cancelamento ou falha durante uma atualização reverte os arquivos já alterados. Se o processo for interrompido abruptamente, reabrir o pack tenta recuperar a atualização registrada. Nenhum resource é iniciado ou reiniciado pelo programa.

## Seleção múltipla e edição em lote

Clique em linhas com **Ctrl** ou **Shift** para destacar vários pacotes. Isso não altera as caixas de inclusão na saída. Com várias linhas destacadas, a barra inferior da tabela permite:

- **Aplicar som:** altera o audioNameHash de todos os veículos dos pacotes destacados.
- **Restaurar sons:** volta aos valores da importação/abertura atual.
- **Destino:** escolhe a pasta individual de todos os pacotes destacados, usada no modo separado.
- **Incluir / Excluir da saída:** modifica as caixas de inclusão dos destacados.

As mudanças ficam na memória até exportar ou salvar. Um som vazio limpa somente o identificador. O programa não importa áudio nem verifica se o som informado existe no servidor.

A caixa do cabeçalho e **Todos/Nenhum** operam sobre todos os pacotes disponíveis, inclusive os ocultos pelo filtro. Clique nos cabeçalhos para ordenar; a busca encontra nomes de pacote ou modelo. Selecionar uma linha abre seus detalhes. Arraste o divisor para ajustar a largura do painel. **Ctrl+O** adiciona arquivos; **F5** analisa as entradas.

Ao acrescentar entradas e analisar novamente, nomes, sons, destinos e escolhas de inclusão já configurados são preservados. Durante a edição de um pack, somente as novas entradas são analisadas; os carros abertos permanecem na coleção.

## Verificação antes da configuração

A análise verifica a seleção atual antes da exportação. Mudanças de inclusão ou nome atualizam essa verificação automaticamente. O painel acima da tabela reúne todos os conflitos encontrados de modelos, assets homônimos com bytes diferentes, IDs/nomes de modkit, handling e definições de layout.

**Montar pack / Salvar alterações** ficam indisponíveis enquanto a verificação estiver pendente ou houver conflitos. Use **Verificar** para repetir a checagem. A gravação também confere a seleção novamente. A verificação não cria o resource nem altera os metadados.

O isolamento de layouts evita a reutilização de suas definições entre pacotes. Outros conflitos, como assets ou IDs de tuning iguais, ainda podem existir entre resources separados. O programa verifica a coleção importada, sem examinar automaticamente todos os resources instalados no servidor. Uma verificação aprovada não substitui o teste de spawn, texturas, tuning, som e animações no FiveM.

## Pastas lembradas

O aplicativo guarda a última pasta de importação, a pasta padrão de exportação, os destinos escolhidos nos diálogos e a pasta do último pack aberto. A pasta padrão reaparece ao iniciar; os seletores retomam suas últimas localizações existentes. Preferências ficam em `%LOCALAPPDATA%\FiveMVehicleInstaller\workspace.json`. Um arquivo de preferências inválido é ignorado, sem bloquear a aplicação. Não são armazenadas cópias dos mods nessa configuração.

No modo separado, um destino individual tem prioridade sobre a pasta padrão. Se todos os selecionados tiverem destinos próprios, a pasta padrão pode ficar vazia. Destinos já existentes nunca são sobrescritos pela exportação normal. A alteração de um pack existente acontece somente pelo fluxo explícito **Abrir pack / Salvar alterações**.

## Compatibilidade da leitura XML

A importação distingue o conteúdo XML de terminadores/preenchimento do arquivo. Bytes NUL finais são ignorados na cópia somente se corresponderem a caracteres NUL completos na codificação detectada e o restante passar por uma leitura XML integral e estrita. UTF-8, UTF-16, UTF-32, BOM e declarações de codificação são respeitados. NULs internos, XML truncado, lixo após o documento e tags divergentes continuam sendo erros. A normalização é registrada no relatório e não modifica o compactado original. Isso evita rejeitar um mod apenas por detalhes de serialização que podem ser tolerados por outros leitores.

O SC300/sc300a local usa cinco bytes NUL depois da tag final de vehicles.meta; esse caso agora é importado normalmente. O caso do Cresta é diferente: seu visibleMods vazio termina em linkMods. A opção de correção explícita na cópia continua disponível para essa alteração estrutural; não há reparo genérico de XML malformado.
## Preservação dos arquivos

Os layouts próprios de cada pacote recebem identificadores exclusivos e referências locais atualizadas automaticamente, inclusive em exportações separadas. O isolamento abrange bancos, animações, portas e demais definições do vehiclelayouts; não depende de importar todos os carros juntos. Referências nativas do GTA, nomes dos modelos e assets não são renomeados. O prefixo é estável por conjunto de modelos: mudar o nome do resource, unir, separar ou reabrir um pack não acrescenta novos prefixos. A análise prepara a cópia em memória, sem alterar a entrada; a gravação ocorre ao exportar ou salvar. Packs antigos indexados recebem o isolamento ao serem abertos e salvos. Resources já instalados sem índice precisam ser reexportados a partir do DLC original.

Fora desses identificadores/referências e do preenchimento NUL final validado, os `.meta` são copiados byte a byte. Alterações de som substituem somente o campo audioNameHash do veículo escolhido na cópia exportada, preservando codificação, BOM, quebras de linha, comentários e demais valores. XML não é reserializado.

Para o erro conhecido em carcols.meta de um bloco vazio `<visibleMods>` fechado por `</linkMods>`, a interface pode oferecer **Aplicar esta correção na cópia**. A proposta só aparece se uma única troca produzir XML válido de carcols e uma nova análise completa passar sem erros. A análise não aplica a proposta; o usuário precisa aprová-la. Outros erros XML e ocorrências ambíguas continuam bloqueados. A aprovação consta do relatório.

O programa isola os identificadores próprios de layouts; não renomeia modelos, assets ou IDs de tuning para contornar conflitos. Os arquivos de entrada são preservados. Não altera server.cfg nem ativa resources.

RPFs referenciados pelo DLC são verificados pela existência do contêiner validado, inclusive quando estão vazios. Um RPF de tuning vazio não representa arquivo ausente e não precisa ser exportado; o relatório registra essa condição. RPFs realmente ausentes ou corrompidos continuam bloqueados.

Metadados ativos do DLC são selecionados pelas declarações de content.xml e pelos sets de startup de setup2.xml. Alternativas externas como Drift Handling e arquivos não ativados ficam fora da exportação. Sem lista declarativa, os cinco tipos suportados são reconhecidos pela raiz XML. Um set ausente gera aviso sem habilitar referências inativas; arquivos necessários ausentes continuam bloqueados. No 180SX de referência, somente handling.meta é ativado: handlingb.meta e handlingstock.meta são alternativas.

## Saída e compatibilidade

- Windows x64; a publicação portátil inclui o runtime .NET 10, sem trimming ou Native AOT.
- RPF7 OPEN/NONE e assets Legacy; não exige GTA, OpenIV ou CodeWalker instalado.
- ZIP, RAR e 7z sem senha; RAR sólido e 7z usam descompactação sequencial.
- Resources separados usam `stream/<arquivo>`; packs usam `stream/<origem>/<arquivo>`. Nomes e bytes dos assets são preservados.
- Metadados permanecem em arquivos separados sob data, sem concatenação de XML.
- O fxmanifest registra explicitamente handling, layouts, veículos, carcols e variações, nessa ordem.
- Assets idênticos de mesmo nome são incluídos uma única vez no pack, mantendo o mapeamento de todas as origens.
- `.fvpack.json` versão 1 permite abrir, atualizar e separar as origens do resource.

Limites: 100.000 arquivos, 16 níveis de containers, 32 GiB de extração temporária e 16 MiB por XML. O índice aceita até 16 MiB, 10.000 origens e 100.000 mapeamentos; o manifest, até 1 MiB. Limpar a coleção ou fechar o aplicativo libera os temporários. Fechar durante uma operação solicita cancelamento; aguarde terminar e feche novamente.

A interface usa IBM Plex Sans incorporada. A licença OFL está em FONT-LICENSE.txt e os créditos/revisões de bibliotecas em THIRD-PARTY-NOTICES.txt. Nenhum mod acompanha o código ou a publicação.

## Desenvolver e validar

SDK 10.0.401 fixado em global.json, permitindo atualizações de patch:

```powershell
.\build.ps1 -Action Build
.\build.ps1 -Action Test
.\build.ps1 -Action Publish -PublishDirectory artifacts\portable\win-x64-attribution
```

A publicação atual está em `artifacts/portable/win-x64-attribution`; ZIP portátil e SHA-256 em artifacts. Testes e prévias ficam em artifacts/test-results e artifacts/preview. A validação funcional e seus limites estão em VALIDATION.md; a discussão de organização/desempenho está em PACKS.md.

A integração do Chaser com RPF de tuning vazio usa FIVEM_EMPTY_CHASER_ARCHIVE. A integração local do SC300 usa FIVEM_SC300_ARCHIVE. O teste dos sete mods RHD usa FIVEM_RHD_SAMPLE_DIRECTORY apontando para a pasta dos compactados originais. Integrações locais opcionais usam FIVEM_CRESTA_ARCHIVE, FIVEM_180SX_ARCHIVE, FIVEM_CEFIRO_ARCHIVE, FIVEM_CHASER_ARCHIVE e FIVEM_CHASER_REFERENCE. O teste do Cefiro importa somente o Add-On e compara hashes com os arquivos originais FiveM como referência, sem importar aquela versão. As amostras antigas mk2100 usam -SampleDirectory, -ManualResourceDirectory e FIVEM_VEHICLE_SAMPLE_ARCHIVE; testes são ignorados quando suas entradas locais não existem.

A auditoria de autoria usa `FIVEM_ATTRIBUTION_SAMPLES` com pastas separadas por ponto e vírgula. `FIVEM_ATTRIBUTION_REPORT` define o arquivo JSON de evidências. O teste analisa cada compactado e libera seus temporários antes do seguinte, sem distribuir os mods.

Core define análise, exportação, verificação e documentos de pack. Infrastructure implementa RPF, compactados, metadados, índice, validação e atualização. App usa WPF/MVVM com preferências locais, seleção múltipla e revisão antecipada. AnalysisSession mantém vivos os containers temporários até terminar o uso dos pacotes.

Exportações novas usam uma pasta de preparação e um rename final sem sobrescrita. Atualizações usam preparação das diferenças, fingerprint do pack, lock de edição e um journal com os arquivos anteriores para rollback/recuperação. Os temporários de atualização ficam ao lado do pack e são removidos após conclusão ou reversão. Não são um backup histórico para desfazer uma edição já salva.

O leitor RPF7 adapta somente o trecho aberto MIT do RageLib, com revisão e créditos preservados. Não inclui código do CodeWalker, chaves ou binários do GTA. SharpCompress usa leitura RAR síncrona na tarefa de trabalho porque a leitura assíncrona dos filtros VM corrompia a amostra original; os testes locais comparam hashes para detectar regressões.
