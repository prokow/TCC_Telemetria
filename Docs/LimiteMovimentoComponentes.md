# Limite de movimento das pecas

O limite foi integrado em `EletronicComponent`, portanto tambem esta disponivel nos scripts que herdam dele. A verificacao fica em `ElectronicComponentMovementLimit`; nao adiciona colliders ou Rigidbody nas pecas. `InstantConnect` e o fluxo de `autoConnectOnAwake` foram preservados. A verificacao posterior corrigiu coordenadas, contatos e liberacao de celulas em `ProtoboardInteractor`; os detalhes estao em `Docs/TestesSampleScene.md`.

## Configurar a cena existente

1. Aguarde a compilacao do Unity e saia do Play Mode.
2. Abra a cena com as pecas, normalmente `Assets/Scenes/SampleScene.unity`, e deixe-a como cena ativa.
3. Execute **Tools > Circuitos VR > Criar area de movimento das pecas**.
4. O comando cria `AreaPermitidaComponentes` com um Box Collider independente, marcado como **Is Trigger**, na layer **Ignore Raycast**. Preenche o campo **Area permitida (Box Collider)** nas pecas da cena, inclusive nas quests inativas. Referencias ja configuradas individualmente sao preservadas. Nao modifica prefabs no disco.
5. Selecione a caixa, use **Edit Collider** e ajuste **Center/Size** para incluir bancada, protoboard e o espaco permitido para as maos. A dimensao inicial apenas engloba as posicoes atuais com uma margem de 75 cm por face; nao substitui esse ajuste para sua sala.
6. Selecione uma peca: em **Limite de movimento no mundo > Ao sair da area**, escolha **Voltar a ultima posicao valida** ou **Soltar e cair**. O padrao e voltar. Salve a cena com Ctrl+S. Ctrl+Z desfaz a configuracao.

O comando pode ser repetido: reutiliza uma referencia existente e preenche somente pecas sem area. Para outro limite, crie um GameObject independente com Box Collider/Is Trigger e arraste-o ao campo da peca. Nunca coloque a caixa dentro da hierarquia da peca: ela se moveria junto com o objeto e o limite sera ignorado. Deixar o campo vazio desativa a funcionalidade nessa peca.

## Comportamento

- A verificacao considera a **posicao do Transform da peca**, nao o volume inteiro da malha. Respeita Center, Size, rotacao e escala da caixa.
- Voltar restaura a ultima posicao/rotacao dentro da caixa, incluindo a rotacao do pivot interno. A selecao da mao e cancelada pela API do XR antes de mover. O Rigidbody conserva a configuracao anterior ao grab, com velocidades zeradas. Isso nao transforma a peca em um objeto fixo: se estava sujeita a gravidade, a fisica continua atuando.
- Cair cancela o grab, mantem a posicao de saida e habilita a gravidade com Rigidbody dinamico. A acao acontece uma vez por saida, permitindo a queda continuar. E necessario ter chao com Collider e layers que colidam com a peca; constraints existentes do Rigidbody continuam valendo.
- A mesma mao nao pode agarrar novamente a peca recuperada ate soltar o grip. A velocidade de arremesso tardia do XR e suprimida somente nessa recuperacao; arremessos normais continuam iguais.
- Qualquer selecao por `ProtoboardInteractor` suspende a recuperacao, mesmo fora da caixa. Isso protege encaixes normais e automaticos. Nao ha reconexao forcada: depois de um retorno, a protoboard continua usando suas regras normais de encaixe.
- A verificacao ocorre depois de Start, em LateUpdate. Uma peca que nasce fora so passa a ser limitada depois de entrar no volume; nao e movida para uma posicao arbitraria. Trocar a referencia da caixa descarta a posicao valida anterior.
- O desligamento da pasta da quest continua tendo prioridade. O limite nao reativa, reaparece ou reparenta pecas de quests finalizadas.

`ConnectorMultimeter` ja possui a restricao de cabo `areaDeLimite`, que foi preservada. Se esse campo estiver configurado, ele continua prendendo a ponta no proprio volume e pode impedir que a ponta ultrapasse o novo limite. Para usar apenas retorno/queda nessa ponta, remova a referencia do limite de cabo no Inspector, sem alterar a protoboard.

## Validacao

Execute:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\ComponentBoundaryChecks\Run.ps1
```

O teste compila as fontes atuais do projeto com as referencias Unity/XRI instaladas, incluindo o Inspector e os comandos de configuracao/auditoria. Executa 53 verificacoes: 44 da geometria local e das transicoes do limite (faces/cantos, centro deslocado, nascimento fora, saidas repetidas, reentrada, troca de volume e imunidade da conexao com a protoboard) e nove das reservas/liberacao de celulas da protoboard.

Esses testes nao executam a fisica ou um headset. O Play Mode automatizado estava indisponivel por licenciamento neste ambiente. Confira em Play Mode:

1. Uma peca com Auto Connect liga nos mesmos pinos ao iniciar, mesmo com a caixa afastada da protoboard.
2. Uma peca livre dentro da caixa pode ser agarrada, girada e solta normalmente.
3. Em Voltar, atravessar cada face com a mao faz a peca retornar; soltar o grip permite pegar de novo.
4. Em Cair, a peca sai da mao e cai sem ser parada a cada frame; confirme a colisao com o chao.
5. Encaixe na protoboard, retire se a peca permitir e confira que o limite volta a atuar. Pecas bloqueadas pelas atividades continuam bloqueadas.
6. Avance de quest com uma peca fora da pasta original e confira que o comportamento de limpeza existente continua funcionando.

As cenas nao foram regravadas automaticamente: a caixa e a associacao sao aplicadas pelo comando acima, com Undo e ajuste visual do tamanho.
