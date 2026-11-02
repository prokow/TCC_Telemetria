# Analise de telemetria no frontend

> Atualizacao: o envio automatico de JSON/CSV e a leitura privada pelo frontend foram implementados posteriormente. Execute a migracao 007 e siga [EnvioTelemetriaSupabase.md](EnvioTelemetriaSupabase.md). As observacoes de armazenamento somente local abaixo descrevem o estado anterior a essa implementacao.

A lista de participantes agora tem **Ver analise** em cada linha. O cadastro, a busca e o login existentes continuam disponiveis.

## Usar agora

1. Reinicie o servidor do frontend: no terminal onde ele esta rodando, pressione Ctrl+C e execute novamente `node --preserve-symlinks-main --preserve-symlinks serve.mjs`, dentro de `Web/participantes-vr`.
2. Abra http://127.0.0.1:4173 e recarregue com Ctrl+F5. Entre com a conta responsavel.
3. Clique em **Ver analise** no cadastro desejado. A identificacao usa o UUID completo, inclusive quando duas pessoas tem o mesmo nome.
4. Para analisar dados que ja estao no banco, execute uma vez `Supabase/005_participant_telemetry_analysis.sql` no SQL Editor do mesmo projeto. Nao precisa editar outro UUID. A migracao requer o schema modular 002 e as permissoes do cadastro 003 ja existentes.
5. Para analisar a coleta local atual do Unity, use **Analisar Session.json do Unity**. O Console do Unity informa a pasta ao concluir o salvamento: `Application.persistentDataPath/Sessoes_Pendentes/<session_id>/Session.json`.

O Unity ainda nao envia essas sessoes ao Supabase pelo fluxo modular. A analise local permite consultar a coleta existente sem implementar um uploader nesta etapa. O arquivo selecionado precisa pertencer ao mesmo UUID do participante aberto; nomes iguais nao autorizam misturar registros.

## Dados apresentados

| Area | Informacoes |
| --- | --- |
| Participante | Nome, UUID, data de cadastro no banco |
| Resumo | Numero de sessoes, tempo total, tentativas, acertos, erros e taxa ponderada de acerto |
| Evolucao | Comparacao de acertos e erros de ate oito sessoes recentes |
| Historico | Inicio, encerramento registrado, duracao, contadores e taxa por sessao |
| Detalhes | UUID de sessao, versao da aplicacao, versao do formato, espaco de coordenadas e origem |
| Atividades | Tentativas, acertos e erros derivados dos eventos, priorizando task_key e usando object_id como fallback |
| Eventos | Tempo relativo ao inicio, tipo, atividade/objeto e descricao; busca e paginacao |
| Headset e controles | Na analise JSON: numero de amostras e proporcao de amostras dos controles marcadas como rastreadas |
| Arquivos | Tipo, tamanho e caminho do arquivo indexado em session_files; sem links publicos para o Storage |
| Exportacao | CSV de todas as sessoes da origem selecionada, com UUIDs, tempos, resultados e versao |

Sessoes sem tentativas nao recebem taxa de zero por cento: a taxa aparece como `—`. O total usa acertos divididos pelo total de tentativas, sem fazer media dos percentuais das sessoes. Contadores divergentes sao sinalizados. Um horario de encerramento indica que houve encerramento registrado; ele nao comprova que a ultima quest foi concluida. A ausencia desse horario tambem nao comprova que uma sessao ainda esta ativa.

## Origem e acesso

**Sessoes do Supabase** e **Arquivos locais analisados** sao origens separadas. A importacao local e feita em um Web Worker, nao envia o arquivo ao servidor e nao cria registros no banco. O mesmo UUID de sessao nao e contado duas vezes ao reabrir um arquivo. Voltar para os participantes ou encerrar o acesso limpa os relatorios locais.

Limites explicitos: 32 MB por arquivo, 20 sessoes locais por participante enquanto a analise estiver aberta, 10.000 eventos por arquivo e 10.000 registros por recurso remoto. A interface pagina 25 sessoes e 50 eventos por vez. Falhas de rede, falta de acesso, arquivos invalidos e respostas incompletas nao sao apresentados como uma analise completa ou como zero sessoes.

A migracao 005 habilita somente leitura de telemetria para contas autenticadas, herdando a visibilidade dos participantes ja definida pelas politicas RLS. Ela nao libera gravacao, leitura anonima nem download publico de arquivos. O cadastro de participantes permanece com as politicas anteriores. A funcao de verificacao de acesso usa `security invoker`; a view existente tambem mantem esse comportamento. Referencias: [RLS no Supabase](https://supabase.com/docs/guides/database/postgres/row-level-security), [funcoes de banco](https://supabase.com/docs/guides/database/functions).

## Arquitetura

- `dist/client.js`: autenticacao, transporte HTTPS e tratamento de acesso expirado/revogado.
- `dist/telemetry.js`: consultas paginadas, validacao, leitura do payload Unity, agregacoes e CSV.
- `dist/analysis-ui.js`: painel por participante, navegacao, filtros, estado de carregamento e cancelamento de respostas antigas.
- `dist/telemetry-worker.js`: leitura e analise do JSON em outra thread.
- `dist/analysis.css`: estilo responsivo, integrado a identidade visual existente.
- `Supabase/005_participant_telemetry_analysis.sql`: preparacao das permissoes de leitura; nao executada remotamente por esta entrega.

## Validacao e entrega

Passaram 46 testes automatizados. Eles cobrem o cliente existente, agregacoes, prioridade de task_key, separacao por UUID, erros de permissao e rede, cancelamento, troca rapida de pessoa, limpeza no logout, importacao local, deduplicacao, CSV, paginacao, Worker real em outra thread e respostas HTTP dos novos arquivos. A verificacao visual em navegador e a consulta com dados reais do Supabase nao foram executadas neste ambiente. Nao houve alteracao dos scripts ou cenas Unity nesta etapa.

Os arquivos locais foram atualizados em `Web/participantes-vr`. A preparacao para publicacao pelo Sites foi tentada, mas o helper nao conseguiu resolver seu proprio caminho no ambiente Windows (`EPERM` em `realpathSync`), antes da sincronizacao do repositorio. Portanto nao ha nova publicacao online confirmada.
