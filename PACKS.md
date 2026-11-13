# Organização de packs e desempenho

Análise em 11/10/2026. Não foi executado um benchmark de FPS, memória, conexão ou inicialização nesta alteração.

## O que a união pode melhorar

O FiveM mantém trabalho por resource. O código oficial de `ResourceFilesComponent::AttachToObject` conecta operações ao início de cada resource, verifica/constrói conjuntos de arquivos e calcula seus hashes; também mantém caminhos de cache por resource. Isso sustenta a existência de custo administrativo, mas não quantifica o ganho de unir veículos. [ResourceFilesComponent.cpp](https://github.com/citizenfx/fivem/blob/master/code/components/citizen-server-impl/src/ResourceFilesComponent.cpp)

O streaming também mantém e processa entradas dos assets do resource. A quantidade e o conteúdo desses assets continuam relevantes depois da união. [ResourceStreamComponent.cpp](https://github.com/citizenfx/fivem/blob/master/code/components/citizen-server-impl/src/ResourceStreamComponent.cpp)

**Inferência:** menos resources podem simplificar distribuição, configuração e operações de início/parada. O código não demonstra que um único resource seja sempre mais rápido. Unir não reduz polígonos, dimensões de texturas ou tamanho de cada arquivo; deduplicar arquivos realmente idênticos é uma economia distinta, verificada pelo instalador.

Alterar um carro dentro de um pack também amplia o conjunto afetado por reiniciar aquele resource. Por isso, para este fluxo, recomendo desenvolver com origens separadas e publicar grupos conforme a manutenção: por categoria, lote ou finalidade. Um resource único para a coleção toda é possível, mas não uma regra universal de otimização.

| Aspecto | Separados | Unificados |
| --- | --- | --- |
| Editar/testar um carro | Origem identificada e reinício independente | Reinício atua sobre todo o pack |
| Distribuição/configuração | Mais resources e linhas de ativação | Um manifest e uma linha de ativação por pack |
| Modelos e texturas | Custo dos assets originais | Mesmo custo, salvo duplicatas idênticas eliminadas |
| Conflitos de nomes/IDs | Podem existir entre resources ativos | Verificados entre as origens selecionadas antes da união |
| Retorno ao desenvolvimento | Já separados | Automático para packs com `.fvpack.json` |

## Como comparar de verdade

Compare a mesma coleção, com os mesmos arquivos e sem manter as versões separada e unificada ativas simultaneamente. Use a mesma versão do servidor/cliente, cenário, carros próximos, configurações gráficas e condições de cache. Registre início do resource, conexão, tamanho distribuído, memória e tempo de quadro em várias execuções. Separe resultados com cache vazio e aquecido.

O profiler oficial ajuda a investigar CPU e scripts; sozinho não mede toda a pressão de texturas/VRAM de um pack de assets. [Using the Profiler](https://docs.fivem.net/docs/scripting-manual/debugging/using-profiler/)

## Estrutura reversível

```text
pack_veiculos/
  fxmanifest.lua
  .fvpack.json
  relatorio.txt
  data/
    carro_a/vehicles.meta
    carro_a/carcols.meta
    carro_b/vehicles.meta
  stream/
    carro_a/carro_a.yft
    carro_a/peca_a.yft
    carro_b/carro_b.yft
```

Resources separados usam `stream/<arquivo>`. Packs mantêm só `stream/<origem>/<arquivo>`, sem reproduzir diretórios `x64`, `levels`, `vehiclemods` ou outros níveis de containers. Os caminhos de `data` permanecem preservados. Packs indexados de versões anteriores são aceitos; a reexportação aplica a organização simplificada.

O manifest lista explicitamente os metadados de todas as origens. Eles permanecem separados, sem concatenar ou reserializar XML. Identificadores próprios de layouts e suas referências recebem isolamento estável por pacote; o restante dos bytes é preservado. As declarações seguem handling, layouts, veículos, carcols e variações, preservando a correção de tuning validada anteriormente.

O índice versão 1 associa cada caminho do pack ao caminho original do resource convertido e ao tipo de metadado. Um asset idêntico pode ser usado por várias origens; na separação, cada origem recebe sua cópia correspondente. Conteúdo atualizado nos caminhos mapeados é preservado ao importar. O índice não guarda cópias antigas dos arquivos e não desfaz uma edição de som já exportada.

Para incluir ou remover origens inteiras, use Abrir pack, desmarque as removidas, analise os novos RPFs e salve as alterações. Os arquivos inalterados são reutilizados sem regravação. Para adicionar/remover/renomear assets individualmente, trabalhe nas origens e importe novamente o DLC. Arquivos suportados não mapeados, ausentes ou com caminhos inconsistentes bloqueiam a separação. Manifests com scripts, áudio, dependências ou Lua dinâmico também são recusados; o aplicativo não executa Lua nem replica comportamento desconhecido.

Packs externos sem índice não são importados. O aplicativo abre e atualiza seus próprios packs com .fvpack.json; a importação de mods usa a versão DLC/RPF. Não infere qual trecho de um XML pertence a cada carro. Essa restrição preserva a unidade dos metadados de cada DLC.

Os nomes das subpastas organizam arquivos; não isolam os nomes de modelos/assets nem IDs no GTA. Desde 23/10/2026, as definições próprias de vehiclelayouts são isoladas pelo conversor com base no conjunto de modelos, inclusive quando a origem é exportada sozinha. Referências nativas permanecem intactas. Packs antigos recebem o isolamento ao abrir e salvar; arquivos de entrada permanecem intactos. O instalador verifica colisões entre as origens selecionadas, porém não examina todos os resources ativos do servidor.
## Edição do pack

O índice versão 1 é usado para recuperar origens, conferir a seleção antes da exportação e atualizar o resource existente. As inclusões, remoções e alterações de som são preparadas e gravadas com controle de mudanças externas e rollback em falha. Não é necessário reimportar os RPFs dos carros que permanecem. Salvar como novo conserva a opção de gerar outro resource. A remoção continua por origem: um DLC com XML compartilhado entre vários carros permanece uma unidade.
