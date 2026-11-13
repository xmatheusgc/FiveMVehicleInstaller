# Validação

## Detecção de autoria — 24/10/2026

- **196 testes aprovados, zero falhas, quatro ignorados** por ausência das referências opcionais antigas/Chaser. Resultado em `artifacts/test-results/attribution.trx`. Integrações de SC300, Cresta, Cefiro, 180SX, Chaser com RPF vazio, sete RHD e auditoria dos 28 compactados de cars/cars2 habilitadas.
- Detecção por créditos explícitos em TXT/Markdown e indícios `by Nome` nos nomes de arquivos/pastas. Nome, função, fonte, trecho e classificação são preservados por origem em `.fvpack.json` e no relatório, inclusive após reabertura, alteração de áudio, renomeação de origem e separação.
- Testes negativos cobrem instruções de instalação, menções a peças/handling, README ambíguo de coleção, outro compactado e documentação dentro de cópia FiveM ready. Textos maiores que 256 KiB são ignorados. READMEs Unicode e cabeçalhos Markdown também foram verificados.
- A auditoria local encontrou atribuições em **22 de 28 pacotes**. Os 11 mods em cars2 declararam conversão por Vsoreny. S14/Jintei, Galant/CipherOG e Cefiro/Akkariin foram confirmados pelas fontes textuais. Seis pacotes ficaram sem evidência reconhecida; a ferramenta não inventa um autor. Evidências em `artifacts/diagnostics/mod-attribution-20261024.json`; detalhes em `docs/autoria-2026-10-24.md`.
- Interface WPF renderizada e inspecionada em 1120 × 800: `artifacts/preview/interface-autoria.png` e `interface-creditos.png`. O teste verifica o nome apresentado e a disponibilidade da fonte/trecho na aba Créditos.
- Compilação Release aprovada. Publicação portátil em `artifacts/portable/win-x64-attribution`; ZIP e checksums em artifacts. Nenhum resource ativo ou servidor foi alterado/reiniciado; teste em jogo não executado.

## Chaser: RPF de tuning vazio — 23/10/2026

- **176 testes aprovados, zero falhas, quatro ignorados** por ausência de referências opcionais antigas/Chaser. Resultado em `artifacts/test-results/empty-rpf.trx`. O novo teste do Chaser afetado e as integrações de SC300, Cresta, Cefiro, 180SX e sete RHD foram habilitados.
- O ZIP `toyota-chaser-jzx100-v1-1_1706457544_852325.zip` inclui `Add-On/dlc.rpf`. Nele, `x64/levels/chaser/vehiclemods/chaser_mods.rpf` realmente existe, tem 512 bytes e é um RPF7 válido sem arquivos. A análise anterior que o classificava como ausente estava incorreta; este achado substitui essa conclusão nos relatórios históricos de 14/10.
- A causa estava no scanner: a expansão mantinha apenas os arquivos internos. Quando um RPF não tinha arquivos, sua existência era perdida, e o seletor de referências do DLC o classificava como ausente por não encontrar descendentes.
- O scanner agora mantém um registro do contêiner após sua leitura bem-sucedida; a seleção distingue RPF vazio validado de RPF ausente. Esses registros não são exportados como assets. Referências realmente ausentes e RPFs corrompidos continuam bloqueados, conforme testes negativos.
- Testes cobrem DLC em pasta e RPF aninhado, exportação e reabertura, manifesto sem contêineres originais e bloqueio em falta/corrupção. O mod real `chaser` exporta oito arquivos: cinco metadados e três assets. Os três assets foram comparados por SHA-256 com os arquivos da versão FiveM incluída no próprio ZIP e são idênticos. O ZIP original permaneceu intacto.
- Diagnóstico anterior à correção em `artifacts/diagnostics/chaser-rpf-20261023/audit.json`, com cópias de content.xml, setup2.xml e dos RPFs. Publicação corrigida em `artifacts/portable/win-x64-empty-rpf`; ZIP e checksums atualizados. Nenhum resource ativo ou servidor foi alterado/reiniciado; teste em jogo não executado.


## Compatibilidade de preenchimento XML — 23/10/2026

- **171 testes aprovados, zero falhas, quatro ignorados** por ausência das referências opcionais antigas/Chaser. Resultado em `artifacts/test-results/xml-compatibility.trx`. Integrações locais de SC300, Cresta, Cefiro, 180SX e os sete mods RHD habilitadas.
- A investigação do SC300 mostrou cinco bytes NUL após um XML completo. Uma rejeição do parser XML estrito não demonstra, por si só, que o mod falharia no GTA. A importação agora separa preenchimento/terminação do arquivo de problemas estruturais do documento.
- `MetadataInput` tenta primeiro a leitura original. Em caso de erro XML, aceita apenas um sufixo de caracteres NUL completos na codificação detectada se a remoção desse sufixo produzir um documento inteiro, válido e estritamente analisável. A fonte não é alterada. O conteúdo usado na exportação contém apenas a remoção desse sufixo e o relatório registra a quantidade de bytes. Não há confirmação adicional para essa compatibilidade de leitura.
- Onze combinações de UTF-8/16/32/Latin-1 e BOM foram testadas, incluindo preservação de comentários/formatos e edição de áudio sobre a cópia preparada. NUL interno, caractere de controle, múltiplas raízes, XML truncado, tags divergentes e lixo após o documento continuam bloqueados. Preenchimento em handling/layouts também foi validado antes do isolamento dos nomes.
- O SC300 original importa automaticamente como sc300a e exporta 48 arquivos; vehicles.meta tem os 5567 bytes esperados, verificados por SHA-256 contra a cópia inspecionada anteriormente. Compactado original preservado. Reabertura de pack também validada.
- O Cresta mantém a correção explícita existente de visibleMods/linkMods, validada novamente contra o RAR original. Ela não é generalizada para consertar arbitrariamente tags.
- Publicação: `artifacts/portable/win-x64-xml-compatibility`; ZIP portátil e checksums em artifacts. Nenhum resource ativo foi substituído ou reiniciado. Testes no jogo continuam sendo uma etapa independente.


## Isolamento de layouts por pacote — 23/10/2026

- Suíte completa em Release: **151 testes aprovados, 0 falhas, 4 ignorados** por ausência das referências locais antigas/Chaser. Resultado em `artifacts/test-results/layout-isolation.trx`. Integrações locais de S14, Chaser90, ER34, Mark II, S13, S15, Naoki S15, Cefiro, Cresta e 180SX habilitadas.
- O teste manual do S14 no servidor confirmou que isolar bancos, portas e animações resolveu o banco errado e a T-pose. A ferramenta aplica agora a mesma estratégia de forma geral a todas as definições locais de vehiclelayouts, mesmo quando o pacote é importado sozinho.
- O identificador de pacote depende do conjunto de modelos, sem depender do caminho do compactado ou nome do resource. Referências por atributo `ref`, ShuffleLink, RearSeatLink e campos de layout/cover/explosion/scenario em vehicles são atualizadas. Referências nativas, bones, nomes de modelos e assets permanecem preservados.
- Testes com os sete mods RHD reais verificam definições distintas entre origens, exportação e reabertura sem novos prefixos. Testes sintéticos cobrem união, separação, renomeação de origem, atualização de pack, migração de índice antigo e correção aprovada de carcols com isolamento de layouts.
- Alterações são pontuais nos bytes: sete combinações de UTF-8/16/32/Latin-1, BOM, quebras, comentários, aspas simples/duplas e atributos em várias linhas foram verificadas. A edição de áudio continua funcionando sobre a cópia isolada.
- Arquivos de entrada e resources ativos não são modificados pela análise. Packs próprios antigos migram ao abrir e salvar; resources sem índice precisam ser reexportados dos DLCs originais. Definições divergentes remanescentes dentro de um pacote bloqueiam a exportação, inclusive separada.
- Publicação portátil em `artifacts/portable/win-x64-layout-isolation`; ZIP e checksums em `artifacts`. A versão genérica foi validada automaticamente; a conferência adicional no jogo permanece manual.


Validação concluída em 14/10/2026: **141 testes aprovados, 0 falhas, 3 ignorados**, em Release/.NET 10. Build dos quatro projetos incluído na execução. Resultados em `artifacts/test-results/validation.trx`.

## RPF, packs editáveis e edição em lote — 14/10/2026

- A importação ignora versões FiveM ready antes da leitura de seus metadados. Somente o Add-On/DLC em RPF, DLCs já extraídos sem manifest e origens de recursos indexados pelo aplicativo são analisados. O leitor de manifest é mantido para validar os recursos próprios com índice. Teste com manifest que lançaria erro e XML FiveM inválido confirma que aquela versão não é executada nem analisada.
- Novos resources separados também recebem .fvpack.json. Packs anteriores com índice versão 1 foram mantidos compatíveis. Recursos externos sem índice são ignorados na importação e recusados pelo fluxo de abertura de pack.
- A validação antecipada retorna juntos conflitos de modelos, assets, modkits, handling e layouts, sem gerar saída. A interface verifica após analisar e mudar inclusão/nome; bloqueia a exportação enquanto pendente ou incompatível. Possui repetição manual e nova verificação antes da gravação. Cancelar a pré-validação não pode continuar para exportação.
- Abrir pack, analisar novas entradas, remover origens desmarcadas e salvar funcionam pelos comandos WPF. Salvar como novo mantém o original. A atualização grava somente arquivos novos/alterados, remove os não usados e regenera manifest/índice; assets idênticos permanecem sem regravação. Compartilhamentos entre origens são preservados, inclusive ao remover a origem que detinha o caminho deduplicado.
- Fingerprints detectam mudanças externas desde a abertura. Atualizações preservam arquivos auxiliares externos e recusam colisão com eles. Lock e journal protegem alterações; rollback e recuperação após interrupção são testados. Os registros são transacionais, sem promessa de backup histórico após salvar.
- Testes cobrem cancelamento na preparação e após a primeira alteração, arquivo sem compartilhamento de exclusão durante o commit, rollback byte a byte, recuperação de journal interrompido e rejeição de caminho que escaparia do pack. Também verificam atualização sem as pastas de importação originais e nova atualização após reabrir.
- Ctrl/Shift destaca várias linhas para aplicar/restaurar som, atribuir destino e incluir/excluir da saída. Caixas de inclusão permanecem independentes do destaque. Testes acionam SelectedItems do DataGrid, comandos e bindings; verificam sons/arquivos resultantes e renderizam a janela em 1360×880 e 1120×760.
- Pastas são persistidas em JSON no LocalAppData; seletores usam últimas importação/exportação/pack. Testes usam preferências isoladas, confirmam restauração e recuperação de JSON inválido. Reanalisar após acrescentar entradas preserva nomes, sons, destinos e inclusões já configurados.
- Integrações locais de Cresta, Cefiro, Chaser e 180SX passaram. A suíte final teve 141 aprovações, zero falhas e três testes antigos ignorados por ausência da pasta Downloads/veiculo-teste. Build Release sem avisos.
- A coleção real Downloads/cars produziu 13 candidatos DLC/RPF e 12 prontos após a correção explicitamente aprovada do Cresta. A alternativa Add-On de chaser ainda referencia um RPF de tuning ausente. Dez conflitos foram apresentados antes da configuração: sete definições de layouts e três peças air_freshener (black, lavender, spice) divergentes entre ER34N e 180SX. A versão FiveM pronta não foi usada para contornar essa falha.
- Em uma cópia isolada do pack anterior com nove origens, foi removido Cresta e salvo com oito, reimportado seu RAR/RPF, aplicada a correção autorizada na cópia e salvo novamente com nove. Som ADDER aplicado em lote a mk2100 e nis13 persistiu após recarregar. Datas de modificação de 1.265 assets mantidos permaneceram intactas; SHA-256 confirmou que todos os 1.316 arquivos do pack anterior continuaram iguais.
- A cópia de integração está em artifacts/pack-edit-validation/20261014-001952/garage. Evidências e conflitos em artifacts/diagnostics/workspace-real-validation.json. Nenhum compactado de entrada, resource instalado ou server.cfg foi alterado. Mods de teste permanecem fora da distribuição.

Publicação em artifacts/portable/win-x64-editable-packs, ZIP e checksums em artifacts. Validação da interface por comandos/bindings e renderizações inspecionadas; não houve controle externo da janela por mouse/teclado. Spawn, tuning, texturas, áudio e animações dessa cópia ainda exigem conferência manual no FiveM.
## Coleção Downloads/cars e Cresta — 13/10/2026

- Reprodução dos comandos reais de análise, seleção, aprovação e exportação do MainViewModel em um Dispatcher WPF. O controle de janelas da habilidade Computer Use não estava disponível nesta sessão: não houve operação do executável por mouse/teclado. A interface foi validada pelos comandos/bindings e por renderização inspecionada, inclusive em 1120×760.
- Os 13 compactados locais produziram 17 candidatos, contando versões FiveM/Add-On alternativas. Com preferência inicial por recursos FiveM válidos e a correção explicitamente aprovada do Cresta, foram exportados os 13 modelos únicos em resources separados.
- No Cresta, o carcols.meta dentro do RAR original já fecha visibleMods com linkMods na linha 10. SHA-256 do arquivo original: B82F01372FBE822F0FE7E0A19BC55DCE48DC0C2CB26CE0A932DB8510AE9620E3. O erro não foi introduzido pela extração. A correção aprovada troca exclusivamente </linkMods> por </visibleMods>. O teste reconstrói os bytes originais a partir da cópia corrigida e confirma esse hash.
- O aplicativo oferece essa correção de forma explícita, sem aplicá-la durante a análise. Reanalisa a proposta antes de oferecê-la; entradas com outras falhas ou ocorrências ambíguas não recebem proposta. Não há renomeação de assets, layouts ou IDs.
- Os motivos de bloqueio aparecem nos detalhes, na atividade e no tooltip de caixas desabilitadas. Erros precedem avisos nos detalhes. O painel de campos tem rolagem para manter as abas acessíveis em janela pequena. A aprovação mantém alterações de som feitas antes dela e registra a correção no relatório de exportação.
- Um set de startup ausente no Add-On do s15naoki causava um bloqueio excessivo, embora os arquivos declarados necessários estivessem inicialmente ativos. Agora gera aviso, sem habilitar referências inativas. Testes cobrem ambos os casos. A versão FiveM válida desse modelo segue preferida.
- A tentativa de união dos 13 modelos foi bloqueada por sete definições de layout divergentes. A avaliação de todos os pares também encontrou colisão de asset air_freshener_black.yft entre ER34N e 180SX e ID de modkit 3190 repetido entre os dois Chasers chaser/jzx100. O diagnóstico registra 13 pares incompatíveis; cada motivo listado é o primeiro bloqueio encontrado naquele par, não uma auditoria exaustiva de todos os conflitos possíveis.
- Gerado um subconjunto de tamanho máximo segundo as verificações atuais, com 9 modelos: ER34N, nisa31, s15naoki, chaser, nis15, cresta, chaser90, mk2100 e nis13. Ficaram fora desse pack s14, jzx100, er34 e 180sx. Isso não certifica compatibilidade em jogo nem com outros resources do servidor.
- A versão Add-On alternativa de chaser continua bloqueada porque content.xml referencia um RPF de tuning ausente no caminho declarado. A versão FiveM válida do mesmo ZIP foi exportada. Alternativas de modelos repetidos permanecem disponíveis na tabela, desmarcadas inicialmente.
- Saída local: C:/Users/mathe/Downloads/cars-convertidos-20261012-234622, com separados/, pack_compativel/, LEIA-ME.txt e diagnostico.json. Comparados SHA-256 dos 1.622 arquivos separados com suas fontes, incluindo 64 metadados. O pack foi reimportado, recuperando 9 origens válidas. A conferência adicional de todos os mapeamentos do índice está em artifacts/diagnostics/collection-pack-verification.json e verificacao-pack.json na saída.
- Não foram alterados compactados de entrada, server.cfg, resources instalados ou processos FiveM. Não há resource parcial da tentativa de união completa. A ativação conjunta dos resources separados ainda pode reproduzir os conflitos apontados.

Build Release sem avisos; 127 testes aprovados, zero falhas, três testes antigos ignorados por ausência da pasta Downloads/veiculo-teste. Integrações locais de Cresta, 180SX, Cefiro e Chaser habilitadas. Publicação em artifacts/portable/win-x64-collection; ZIP e SHA-256 atualizados em artifacts. Spawn, texturas, tuning, direção/animação e som da nova coleção permanecem para conferência manual no FiveM.
## Seleção de metadados ativos / 180SX — 12/10/2026

- O 180SX instalado declarava handling.meta, handlingb.meta e handlingstock.meta, com três definições diferentes do mesmo handlingName `180sx`. O content.xml original declara e ativa somente handling.meta; as outras duas variantes não são referenciadas.
- A seleção anterior verificava a existência das referências do DLC, mas depois exportava todos os XMLs reconhecidos. Agora usa a lista declarativa ativa para filtrar metadados, antes da validação de XMLs não utilizados. Respeita GROUP_STARTUP, disabled e listas de ativação/desativação. Um arquivo inicialmente ativo não é descartado porque outro arquivo está em filesToEnable.
- Sem referências declarativas, mantém a descoberta por raiz XML. Definições diferentes do mesmo handlingName no pacote geram erro explícito, inclusive ao importar um resource com glob de handling. Não escolhe pelo nome handling.meta e não reescreve metadados. Múltiplos arquivos declarados com IDs diferentes continuam aceitos.
- Testes sintéticos cobrem alternativas inativas, alternativa não referenciada com XML inválido, nome personalizado do handling ativo, change set alternativo fora de GROUP_STARTUP, arquivo inicialmente ativo, desativação explícita, ambiguidade sem referências e em manifest FiveM, além de múltiplos IDs legítimos.
- A integração com o ZIP original `e97292-DMC 180sx 1.2c.zip` exporta 169 assets e quatro metadados ativos (173 arquivos), compara todos os bytes com suas fontes, confere uma única declaração HANDLING_FILE e reimporta o resource.
- O manifest do 180SX instalado foi corrigido removendo somente as referências às duas alternativas, tanto de files quanto de data_file. Os seis arquivos .meta na pasta data foram preservados e seus SHA-256 conferidos antes/depois; as alternativas continuam no disco. Manifest anterior salvo em artifacts/diagnostics/180sx.fxmanifest.before-handling-fix.<data-hora>.lua e hashes em 180sx-handling-manifest-fix.json. Não foi alterado server.cfg nem reiniciado o resource/servidor.

Resultado: build Release sem avisos, 118 testes aprovados, zero falhas e três testes antigos ignorados pela ausência da pasta Downloads/veiculo-teste. Integrações locais de 180SX, Cefiro e Chaser habilitadas. A atualização portátil está em artifacts/portable/win-x64-handling, com ZIP e checksums atualizados. A aplicação do handling no jogo exige recarregar o resource e criar um veículo novo; essa conferência permanece manual.
## FiveM pronto e investigação de layouts — 12/10/2026

- O ZIP real do Cefiro contém Add-On e FiveM. O parser anterior bloqueava a versão FiveM em `url`; agora aceita essa autoria literal e trata a declaração antiga de idioma `DLCTEXT_FILE` com exclusão e aviso explícito. Outros tipos desconhecidos e resources com scripts/áudio/dependências continuam bloqueados.
- A tabela identifica a origem como FiveM pronto ou DLC / Add-On. Havendo modelos repetidos, a seleção inicial prefere um resource FiveM válido e desmarca a alternativa, sem removê-la. O teste WPF exporta a seleção inicial e depois permite escolher a alternativa.
- O teste do ZIP real exporta ambas as versões, compara SHA-256 de todos os assets e cinco metadados com a versão FiveM do criador, verifica o layout e reimporta as saídas. Não inclui o mod na distribuição.
- A análise detecta definições de layout homônimas e divergentes entre pacotes. Mostra avisos por pacote e na atividade; a união bloqueia conflitos. Os testes cobrem layouts, bancos, animações e pontos de entrada, além de aceitar definições idênticas com comentários, formatação e ordem de atributos diferentes.
- A auditoria local dos nove compactados originais e dez resources instalados realizou 66 comparações de metadados: 53 byte a byte idênticas e 13 com diferenças somente no valor XML do som. Todos os 12 pares de vehiclelayouts.meta eram byte a byte idênticos. O log do cliente confirma o carregamento dos layouts antes dos veículos.
- Foram encontrados sete nomes de layout/banco/animação/ponto de entrada com definições divergentes e dois resources ativos definindo ER34N. O relatório `artifacts/diagnostics/vehicle-layout-investigation.md` separa fatos e hipótese; hashes e definições estão em `vehicle-originals-audit.json`. Os conflitos são uma hipótese para a T-pose; não houve reprodução nem confirmação de correção no jogo.
- Durante a validação surgiram falhas intermitentes no rename final da pasta temporária no Windows, sem perda de metadados. A gravação agora repete somente esse rename em bloqueios transitórios, por até 900 ms, mantendo cancelamento, validação de caminhos e proibição de sobrescrita. Um teste abre um handle real sem compartilhamento de exclusão e comprova a conclusão após sua liberação.

Resultado: build Release sem avisos; 110 testes aprovados, zero falhas. Três testes antigos da pasta extraída mk2100 foram ignorados porque `Downloads/veiculo-teste` não está mais disponível. Os compactados originais, incluindo mk2100, foram auditados separadamente; Cefiro e Chaser tiveram integração automatizada habilitada. Não houve modificação de resources instalados nem server.cfg. A confirmação de bancos/animações no FiveM permanece manual.

Publicação em `artifacts/portable/win-x64-layouts`, ZIP e checksums atualizados em `artifacts`. Prévias da tabela com origem renderizadas e conferidas, incluindo o tamanho mínimo.
## Tabela e tipografia — 12/10/2026

- A lista de pacotes foi substituída por uma tabela com nome, modelos, arquivos, tamanho, destino e estado. Cabeçalhos permitem ordenar; seleção da linha abre os detalhes e as caixas definem os pacotes da exportação. Busca e seleção em lote continuam disponíveis.
- Detalhes editáveis permanecem à direita, com divisor ajustável. Destinos individuais são campos visíveis no modo separado. Exportação, modo e pasta padrão ficam no rodapé; a navegação lateral e os cartões de contagem foram removidos.
- IBM Plex Sans regular, medium e semibold foram incorporadas como recursos WPF. O teste resolve cada peso pela URI do assembly, verificando que não depende de instalação no Windows. Licença OFL e revisão da fonte constam da distribuição e dos créditos.
- Testes WPF exercitam o clique real do cabeçalho de ordenação, seleção de linha, seleção em lote/mista, busca, edição do som, destinos independentes e os modos separado/unificado. Verificam a largura de todas as colunas e a ocupação da tabela em 1360×880 e 1120×760.
- Prévias com dados sintéticos foram renderizadas e inspecionadas: coleção, tabela, tamanho mínimo, pack, estado inicial, nome inválido e falha de importação. A renderização aguarda o Dispatcher para concluir a distribuição das colunas. Arquivos em `artifacts/preview`, incluindo `interface-colecao.png` e `interface-nomes.png`.
- A integração local continua comparando pasta/RAR do mk2100, ciclo unir/separar, extração manual e Chaser. As referências atuais são `resources/[vehicles]/mk2100` e `resources/[vehicles]/jzx100`. O RAR do mk2100 está fora da pasta da amostra e é informado por `FIVEM_VEHICLE_SAMPLE_ARCHIVE`.

Build Release sem avisos; 103 testes aprovados, sem falhas ou ignorados. Esta alteração não modifica conversão, arquivos do servidor, metadados ou manifests instalados. A publicação fica em `artifacts/portable/win-x64-table`, preservando o executável anterior aberto. ZIP e checksums em `artifacts`.
## Stream simplificado — 11/10/2026

A exportação anterior reproduzia diretórios de containers/DLC. No `toyota-chaser-jzx100`, 54 assets estavam distribuídos em oito subpastas sob `stream/x64/levels/...`.

- Resources separados agora exportam todos os modelos, texturas e peças diretamente em `stream/<arquivo>`, preservando nomes e bytes. Metadados em `data` mantêm seus caminhos e conteúdos.
- Packs usam apenas `stream/<origem>/<arquivo>`. Índices versão 1 com caminhos anteriores continuam aceitos; reexportar aplica o formato simplificado. A união/separação, som editado e assets compartilhados continuam cobertos.
- Assets homônimos continuam bloqueando conversão quando podem causar colisões. Nenhum modelo é renomeado e nenhum destino é sobrescrito para acomodar a nova organização.
- Testes sintéticos reproduzem os caminhos do Chaser, outros layouts de DLC, subpastas internas de RPFs e uma pasta `stream` já existente. Conferem bytes de assets e metadados, ausência de subpastas e reimportação.
- Integração adicional converte o RAR original do Chaser e seu resource existente. Os 54 assets foram comparados por SHA-256 com a referência, os cinco metadados foram comparados com suas fontes e ambas as saídas puderam ser reimportadas.
- A comparação de referência do `mk2100` localiza assets pelo nome original, aceitando a referência antiga com subpastas e a estrutura nova direta. Os 139 assets permanecem idênticos, incluindo as 135 peças de tuning.

Execução final: 102 testes aprovados, 0 falhas, 0 ignorados, com Chaser, pasta/RAR do mk2100 e referência manual local habilitados. Build Release dos quatro projetos concluído sem avisos.

Também foram reorganizados os seis resources existentes na pasta `[vehicles]`, sem reexportar ou regravar seus metadados:

| Resource | Assets preservados | Subpastas removidas |
| --- | ---: | ---: |
| nissan-180sx-type-x | 169 | 1 |
| nissan-silvia-ks-1998-d14-kouki | 31 | 1 |
| nissan-silvia-s13 | 326 | 1 |
| nissan-silvia-s15-spec-r | 194 | 1 |
| toyota-chaser-jzx100 | 54 | 8 |
| toyota-mark-2-jzx100 | 139 | 1 |

Foram movidos 894 arquivos e removidas somente 13 pastas vazias. Os hashes dos 913 assets e de 44 arquivos preservados (incluindo todos os metadados, manifests, relatórios e `server.cfg`) foram conferidos antes/depois. Não havia nomes duplicados nem índices de pack nesses resources. O registro com caminhos anteriores, novos caminhos e hashes está em `artifacts/diagnostics/stream-layout-migration.json`.

A atualização portátil fica em `artifacts/portable/win-x64-flat-stream`, com ZIP e checksums atualizados. O aplicativo aberto anterior foi preservado. O servidor não foi reiniciado; os resources precisam ser recarregados para atualizar o índice de streaming. A conferência no jogo da nova organização permanece manual.

## Correção dos nomes dos resources — 11/10/2026

A interface permitia editar nomes com espaços ou letras maiúsculas e continuava mostrando “Pronto”, mas o exportador exigia nomes em minúsculas. Trocar somente espaços por hífens não resolvia os nomes mostrados na captura do usuário.

- A interface normaliza maiúsculas e espaços para a pasta de saída, mostra o nome resultante antes de exportar e aplica a mesma regra ao nome do pack e às origens dentro dele.
- Nomes inválidos recebem indicação imediata; a exportação fica desabilitada até corrigir ou desmarcar os pacotes afetados. A validação também ocorre no comando, antes de abrir o seletor de destino.
- A regra compartilhada no núcleo mantém o backend estrito. Nomes reservados, caminhos, nomes vazios, nomes com mais de 64 caracteres e quebras de linha finais na API são rejeitados.
- Os cinco nomes da captura foram exercitados por comandos/bindings WPF, incluindo a edição do TextBox. Os arquivos `vehicles.meta` foram comparados byte a byte e os nomes dos modelos conferidos após exportar.
- União com nome do pack normalizado foi reimportada; a origem normalizada foi recuperada. Colisões após normalização em lote não criam saídas nem sobrescrevem resources.
- As prévias `interface-nomes.png` e `interface-nome-invalido.png`, em 1120×800, foram renderizadas e inspecionadas. Não foi necessário alterar modelos, metadados ou configuração do servidor.

A execução final usou a pasta de referência atual `resources/[vehicles]/toyota-mark-2-jzx100`; todos os 98 testes passaram, sem testes ignorados. A publicação corrigida fica em `artifacts/portable/win-x64-names`, preservando a versão anterior aberta em `win-x64-workspace`. O ZIP portátil e seus checksums foram atualizados.
## Organização de packs e nova interface — 11/10/2026

- Descoberta de vários resources em uma pasta, leitura declarativa de `fxmanifest.lua`/`__resource.lua`, globs e exclusão de alternativas não declaradas.
- União em um resource e separação por origem, com subpastas, texturas compartilhadas e edição de som. Comparação dos bytes após separar e unir novamente; apenas relatórios variáveis são excluídos dessa comparação.
- Bloqueio de modelos, assets, modkits, handling e nomes conflitantes. Assets idênticos compartilhados são deduplicados; os divergentes são recusados.
- Manifests com scripts, áudio, dependências e Lua dinâmico são bloqueados. Não há execução de Lua.
- Índices com versão desconhecida, caminhos maliciosos, grupos duplicados, arquivos ausentes ou novos assets não mapeados são rejeitados. Uma entrada independente válida continua sendo analisada.
- Cancelamento e destinos existentes não deixam resource parcial nem sobrescrevem arquivos.
- Testes WPF com comandos e bindings reais: dois pacotes em destinos individuais, sem pasta padrão; seleção em lote e busca; união ignorando destinos individuais; reimportação e separação; atividade acessível mesmo sem pacotes.
- Cinco prévias da interface foram renderizadas e inspecionadas: coleção, pack, janela compacta em 1120×800, estado inicial e falha de importação. Arquivos em `artifacts/preview`. A edição de som permanece acessível no tamanho mínimo.
- Integração local adicional: `mk2100` e outro pacote unidos, reimportados e separados; comparação SHA-256 de todos os 139 assets e cinco metadados com suas fontes. Som `SULTAN`, peças em `vehiclemods` e textura interna compartilhada preservados.

O novo fluxo de pack unificado **não foi testado dentro do FiveM** nesta alteração. A confirmação anterior de tuning se refere ao resource individual corrigido. Não foi realizado benchmark de desempenho; a análise e suas limitações estão em `PACKS.md`.

Para conferir o pack no jogo, ative somente a versão unificada da coleção, abra um processo novo do FiveM e teste spawn, texturas, peças e som de cada modelo. Desative versões separadas equivalentes para não introduzir colisões. Esse procedimento permanece manual; o aplicativo não altera `server.cfg` nem inicia resources.
## Correção investigada em 11/10/2026

O `mk2100` aparecia no jogo, mas suas modificações não. O usuário confirmou que a extração manual apresentava a mesma falha.

- Os 139 assets (quatro arquivos principais/texturas e 135 peças de tuning) foram comparados com a extração manual: bytes idênticos. Na validação anterior foram comparados também os caminhos relativos. Com stream simplificado, a comparação de assets usa seus nomes originais e hashes, mantendo a conferência dos caminhos dos metadados.
- `carcols.meta`, `carvariations.meta` e `vehiclelayouts.meta` coincidem com a referência manual. O `vehicles.meta` coincide byte a byte quando a troca de som é explicitamente igualada ao som da referência.
- O handling da referência foi personalizado manualmente durante a investigação. O teste respeita essa diferença: compara o handling exportado com a entrada original, sem importar a configuração personalizada do servidor.
- O manifest gerado usava a ordem alfabética dos caminhos, carregando carcols e variações antes de veículos. O manifest da extração manual também tinha essa ordem. Os logs do cliente confirmam esse carregamento.
- Corrigida a geração para declarar handling, layouts, veículos, carcols e variações, nessa ordem. O teste verifica a ordem por tipo, com vários arquivos de nomes diferentes, e compara os metadados com suas entradas.
- Foi corrigido somente `fxmanifest.lua` no resource de teste do servidor; os hashes dos cinco `.meta` foram conferidos antes e depois. A cópia anterior do manifest está em `artifacts/diagnostics/Toyota-Mark-II-JZX100.fxmanifest.before.lua`. O servidor e o cliente não foram reiniciados pelo instalador.

O FiveM antecipa handling/layouts e preserva a ordem relativa dos demais tipos: [código oficial de LoadDataFiles](https://github.com/citizenfx/fivem/blob/master/code/components/gta-streaming-five/src/LoadStreamingFile.cpp#L2403-L2439). Em 11/10/2026, após a correção do manifest e o procedimento de recarregamento solicitado, o usuário confirmou que as peças voltaram a aparecer no vMenu. A recuperação da exibição das modificações foi confirmada no jogo, sem alteração dos metadados de tuning.

Os logs também registram peças originais com uso de memória física de 64 a 96 MiB e alertas de streaming. Esses arquivos foram preservados; os alertas não demonstram, por si só, a causa da ausência de todas as modificações.

## Automática

```powershell
.\build.ps1 -Action Test
.\build.ps1 -Action Test -SampleDirectory 'C:\Users\mathe\Downloads\veiculo-teste' -ManualResourceDirectory 'C:\caminho\para\resource-extraido-manualmente'
```

A suíte verifica índices RPF7, subpastas, descompressão, cabeçalhos RSC7, recursos acima de 16 MiB, criptografia não suportada, ZIP, 7z, XML, manifests, colisões, arquivos ausentes, cancelamento e destinos existentes.

Os testes de som comparam o resultado com os bytes esperados, preservando BOM, UTF-8, UTF-16 LE/BE, UTF-32 e Latin-1, CRLF/LF/CR, namespaces, comentários, entidades, atributos e espaços. Cobrem vários veículos, campos vazios/ausentes, CDATA e troca sem alteração efetiva. Nenhum XML é serializado na exportação; apenas o trecho do campo escolhido é substituído.

O teste WPF roda em thread STA com Dispatcher, sem janela visível. Analisa um pacote, edita o som pelo TextBox/binding e exporta pelos comandos da interface. Uma prévia PNG é gravada quando `FIVEM_UI_PREVIEW_ROOT` está definido.

Com `-SampleDirectory`, converte a pasta e o RAR reais do `mk2100`, compara os hashes dos assets, confere tuning, textura compartilhada, layouts, preservação de todos os metadados sem edição e exclusão do Drift Handling externo. Sem mod local, o teste é ignorado.

Com `-ManualResourceDirectory`, compara a exportação com os arquivos locais extraídos manualmente. Sem as duas entradas locais, esse teste é ignorado. Os mods não são incluídos no projeto ou na publicação portátil.

## Conferência manual no FiveM — peças no vMenu confirmadas

Confirmação do usuário em 11/10/2026: após a correção, as peças apareceram no vMenu. A aplicação visual de cada peça, texturas, layout e som não foram confirmados individualmente nesta resposta. O roteiro abaixo permanece disponível para essa conferência complementar.

1. Recarregue o resource cujo manifest foi corrigido, ou instale uma nova exportação com o aplicativo atualizado.
2. Feche completamente o FiveM e abra novamente; a validação não deve depender apenas de reconnect ou restart do resource.
3. Faça spawn de um novo `mk2100`; confira exterior, interior e texturas.
4. Confira as opções e a aplicação das peças de tuning, além do layout do motorista.
5. Confira o som original ou o identificador explicitamente escolhido na exportação.
6. Se continuar sem tuning, diferencie opções ausentes de peças selecionáveis que não renderizam, e capture os logs F8 após uma sessão nova.

Há também um [relato aberto de falha no tuning após reconexão](https://github.com/citizenfx/fivem/issues/4248), sem comprovação de que seja o mesmo caso deste veículo. Por isso a conferência requer um processo novo do cliente.

Os testes de arquivos não comprovam comportamento dentro do jogo. Nenhum `.meta` existente no servidor ou configuração `server.cfg` foi alterado por esta correção.
