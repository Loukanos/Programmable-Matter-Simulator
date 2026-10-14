# Manual do Usuário do Programmable Matter Simulator

Versão v1.0 (TCC III)

Este manual ensina a criar os dois arquivos que o simulador lê (o arquivo de comportamento e o
arquivo de cenário) e a usar a interface para rodar e analisar uma simulação. Não é preciso saber
programar em C# nem conhecer o Unity: basta um editor de texto (por exemplo, o Bloco de Notas ou
o Visual Studio Code) para escrever os arquivos JSON.

## Sumário

1. [Conceitos básicos](#1-conceitos-básicos)
   - [1.1 A grade](#11-a-grade)
   - [1.2 Partículas e tipos](#12-partículas-e-tipos)
   - [1.3 O ciclo](#13-o-ciclo)
   - [1.4 O que acontece dentro de um ciclo](#14-o-que-acontece-dentro-de-um-ciclo)
   - [1.5 Mensagens](#15-mensagens)
   - [1.6 Colisões e movimentos inválidos](#16-colisões-e-movimentos-inválidos)
   - [1.7 Quando a simulação termina](#17-quando-a-simulação-termina)
2. [Arquivo de comportamento](#2-arquivo-de-comportamento)
   - [2.1 Estrutura geral](#21-estrutura-geral)
   - [2.2 Como escrever valores](#22-como-escrever-valores)
   - [2.3 Eventos](#23-eventos)
   - [2.4 Condições](#24-condições)
   - [2.5 Operadores lógicos](#25-operadores-lógicos)
   - [2.6 Operadores de comparação](#26-operadores-de-comparação)
   - [2.7 Ações](#27-ações)
   - [2.8 Funções de valor](#28-funções-de-valor)
   - [2.9 Direções](#29-direções)
   - [2.10 Operação relativa no setState](#210-operação-relativa-no-setstate)
   - [2.11 Condições com nome (definitions)](#211-condições-com-nome-definitions)
   - [2.12 Mensagens de erro comuns](#212-mensagens-de-erro-comuns)
3. [Arquivo de cenário](#3-arquivo-de-cenário)
   - [3.1 Campos](#31-campos)
   - [3.2 Validações](#32-validações)
4. [Tutorial da interface](#4-tutorial-da-interface)
   - [4.1 Passo a passo com o exemplo 1](#41-passo-a-passo-com-o-exemplo-1)
   - [4.2 Referência dos controles](#42-referência-dos-controles)
   - [4.3 Painel de indicadores](#43-painel-de-indicadores)
   - [4.4 Exportação dos indicadores (CSV)](#44-exportação-dos-indicadores-csv)
5. [Exemplos completos](#5-exemplos-completos)
   - [5.1 Exemplo 1: até a borda](#51-exemplo-1-até-a-borda)
   - [5.2 Exemplo 2: cadeia que repassa uma mensagem](#52-exemplo-2-cadeia-que-repassa-uma-mensagem)
   - [5.3 Exemplo 3: contagem de saltos](#53-exemplo-3-contagem-de-saltos)

---

## 1. Conceitos básicos

### 1.1 A grade

A simulação acontece numa grade tridimensional de células, como um tabuleiro em 3D. O tamanho da
grade é definido no arquivo de cenário, em número de células por eixo: `[x, y, z]`.

- As posições são números inteiros. Em cada eixo, vão de 0 até o tamanho menos 1. Numa grade
  `[8, 2, 1]`, por exemplo, `x` vai de 0 a 7, `y` de 0 a 1 e `z` só pode ser 0.
- Cada célula comporta no máximo uma partícula.
- O eixo **Y é a vertical**: `Y+` aponta para cima e `Y-` para baixo. X e Z são as direções
  horizontais.
- A grade é fechada: nenhuma partícula sai dela. Tentar sair conta como movimento inválido
  (veja a [seção 1.6](#16-colisões-e-movimentos-inválidos)).

### 1.2 Partículas e tipos

Cada partícula tem um **id** (número único), um **tipo** e uma **posição**. Ela também pode guardar
**variáveis** próprias (criadas com a ação `setState`) e tem uma **cor**.

O **tipo** é o nome que liga a partícula às suas regras no arquivo de comportamento. Todas as
partículas do mesmo tipo seguem as mesmas regras, mas cada uma tem a sua posição e as suas
variáveis.

Uma partícula só sabe três coisas:

- os valores das suas próprias variáveis;
- quais das suas seis faces (`X+`, `X-`, `Y+`, `Y-`, `Z+`, `Z-`) têm uma vizinha encostada;
- as mensagens que recebeu.

Ela não consegue ler as variáveis de outra partícula. Toda informação entre partículas passa por
mensagens. Vizinhas são apenas as que encostam numa face; diagonais não contam.

### 1.3 O ciclo

O tempo da simulação avança em passos chamados **ciclos**. Em cada ciclo, todas as partículas
decidem o que fazer olhando a situação do **início do ciclo**, e todos os movimentos são aplicados
juntos, no final. Por isso, a ordem em que as partículas aparecem no arquivo não dá vantagem a
nenhuma delas na hora de se mover.

Antes do primeiro ciclo, cada partícula executa uma vez o seu evento `onInit` (preparação). O
indicador **Ciclos** mostra quantos ciclos já foram concluídos.

### 1.4 O que acontece dentro de um ciclo

1. As mensagens enviadas no ciclo anterior são entregues.
2. Cada partícula executa as suas regras, nesta ordem:
   1. `onMessage`, uma vez para cada mensagem recebida;
   2. `onTouch`, se houver vizinha encostada;
   3. `onStep`, sempre.

   Dentro de cada evento, as regras rodam de cima para baixo; dentro de cada regra, as ações rodam
   na ordem em que foram escritas.
3. O simulador resolve as colisões.
4. Os movimentos são aplicados, o ciclo é contado e o simulador verifica se deve encerrar.

Três consequências importantes para prever o resultado:

- **`setState` e `setColor` valem na hora**, para a própria partícula. Uma regra que vem depois, no
  mesmo ciclo, já enxerga o novo valor. Por exemplo, se uma regra do `onMessage` grava
  `setState('parar', true)`, o `onStep` desse mesmo ciclo já vê `parar` igual a `true`.
- **`move` só registra a intenção.** A posição muda no fim do ciclo. Funções como `hasNeighbor` e
  `canMove` sempre usam as posições do início do ciclo, mesmo depois de um `move`.
- **Só o primeiro `move` do ciclo vale.** Se várias regras com condição verdadeira pedirem
  movimento, conta o primeiro na ordem acima; os outros são ignorados. Isso vale mesmo se o
  primeiro for inválido.

### 1.5 Mensagens

A ação `sendMessage` envia uma mensagem para uma vizinha específica ou para todas as vizinhas. A
mensagem leva uma **etiqueta** (um texto ou número que identifica o assunto) e, se você quiser, uma
**carga** numérica.

- **Entrega no ciclo seguinte.** Uma mensagem enviada no ciclo N chega no início do ciclo N+1.
  Mensagens enviadas no `onInit` chegam no ciclo 1.
- **Duração de um ciclo.** A mensagem fica disponível só durante o ciclo em que chegou; no ciclo
  seguinte ela some.
- **Destino decidido no envio.** As vizinhas são as do início do ciclo em que a mensagem foi
  enviada. Se não houver vizinha na direção escolhida, ninguém recebe, mas a tentativa conta no
  indicador "Mensagens enviadas".
- **Lado de origem.** A mensagem guarda de que lado a remetente estava, visto pela destinatária.
  Uma mensagem enviada para `X+` chega pelo lado `X-` de quem recebe.
- **Ordem de chegada.** Quando várias mensagens chegam no mesmo ciclo, elas são tratadas na ordem
  em que as remetentes aparecem no arquivo de cenário.

Para reagir a mensagens, use o evento `onMessage` ou a condição `checkMessage`.

### 1.6 Colisões e movimentos inválidos

- Se **duas ou mais partículas** pedem a mesma célula no mesmo ciclo, **nenhuma** se move. Cada uma
  conta uma colisão.
- Se a célula pedida está ocupada por uma partícula que **não vai sair** (porque ficou parada ou
  porque também foi bloqueada), o movimento é bloqueado e conta uma colisão.
- Se a célula está ocupada por uma partícula que **sai no mesmo ciclo**, o movimento acontece, como
  numa fila que anda. Duas vizinhas que se movem uma para a célula da outra trocam de lugar.
- Um movimento para **fora da grade**, ou com uma direção que não existe (lida de uma variável), é
  um **movimento inválido**: a partícula fica onde está.

No ciclo seguinte, a partícula pode consultar o que aconteceu com a função `moveResult()`.

### 1.7 Quando a simulação termina

A simulação encerra sozinha por um destes motivos, definidos no arquivo de cenário:

| Critério | Campo | Texto do motivo |
|---|---|---|
| Atingiu o número máximo de ciclos | `maxCycles` | `atingiu o limite de N ciclos` |
| Ficou N ciclos seguidos sem nenhuma atividade | `idleCyclesLimit` | `sem atividade por N ciclo(s)` |

- **Atividade** é qualquer um destes acontecimentos num ciclo: um movimento realizado, um
  `setState`, um `setColor` ou um `sendMessage` que chegou a pelo menos uma vizinha. Gravar numa
  variável o mesmo valor que ela já tinha também conta como atividade. Por isso, uma regra que faz
  `setState` em todo ciclo impede o encerramento por inatividade.
- Se os dois critérios forem atingidos no mesmo ciclo, vale o limite de ciclos.
- Com `maxCycles` igual a 0 e `idleCyclesLimit` igual a 0, a simulação só para quando você pausa ou
  encerra.

O motivo aparece na linha de status ("Simulação encerrada: ...") e no arquivo CSV de indicadores.

---

## 2. Arquivo de comportamento

### 2.1 Estrutura geral

O arquivo de comportamento é um objeto JSON. Cada chave de primeiro nível é o **nome de um tipo**
de partícula. A única exceção é a chave opcional `definitions`, que guarda condições com nome
(veja a [seção 2.11](#211-condições-com-nome-definitions)).

```text
{
  "definitions": { "nome da condição": "condição" },
  "NomeDoTipo": {
    "evento": [ regra, regra, ... ],
    "outro evento": [ ... ]
  },
  "OutroTipo": { ... }
}
```

Cada **regra** é um objeto com dois campos:

- `condition` (opcional): um texto com a condição. Sem condição (ou com o texto vazio), a regra
  vale sempre.
- `actions` (obrigatório): uma lista de textos, cada um com uma ação. A lista não pode ser vazia.

Exemplo completo, com um tipo chamado `Queda` que desce até encostar em alguma coisa:

```json
{
  "Queda": {
    "onInit": [
      { "actions": ["setColor('#3399FF')"] }
    ],
    "onStep": [
      {
        "condition": "hasNeighbor('Y-')",
        "actions": ["setColor('#FF6600')"]
      },
      {
        "condition": "canMove('Y-')",
        "actions": ["move('Y-')"]
      }
    ]
  }
}
```

Regras gerais:

- Os nomes de tipo **diferenciam maiúsculas de minúsculas** (`Elo` e `elo` são tipos diferentes) e
  precisam ser iguais aos usados no arquivo de cenário. Um tipo que nenhuma partícula usa é
  permitido.
- Nenhum evento é obrigatório. Um tipo pode até ser um objeto vazio (`{}`), e então suas
  partículas não fazem nada.
- Uma lista de regras vazia (`[]`) é permitida.
- Nenhum outro campo é aceito numa regra. Escreva `condition`, `actions` e `definitions` em
  minúsculas, exatamente assim: `Condition` ou `DEFINITIONS`, por exemplo, são recusados (veja a
  [seção 2.12](#212-mensagens-de-erro-comuns)).
- As condições e ações são textos dentro do JSON, que já usa aspas duplas para delimitá-los. Por
  isso, dentro delas, use **aspas simples**: `"move('X+')"`.

### 2.2 Como escrever valores

| Tipo de valor | Como escrever | Observações |
|---|---|---|
| Número | `3`, `-2`, `2.5` | Use **ponto** como separador decimal. A vírgula separa argumentos: `2,5` são dois argumentos. |
| Texto | `'ativo'` | Entre aspas simples. Uma palavra sem aspas também vira texto (`ativo` vale `'ativo'`), mas prefira as aspas. |
| Verdadeiro e falso | `true`, `false` | Sem aspas. |
| Direção | `'X+'` ou `X+` | Veja a [seção 2.9](#29-direções). |

Detalhes que evitam surpresas:

- Um texto entre aspas que parece número vira número: `'5'` vale 5. Da mesma forma, `'true'` e
  `'false'` viram verdadeiro e falso.
- Comparações de texto não diferenciam maiúsculas de minúsculas: `'Ativo'` é igual a `'ativo'`.
- O nome de uma variável pode ser escrito com aspas (`'energia'`) ou sem (`energia`), mas não pode
  ser `true` nem `false`.
- As funções de valor (como `myId()`) precisam sempre dos parênteses quando usadas como valor.
  Sem eles, `myId` é lido como o texto "myId".
- Os nomes de eventos e de funções e as palavras `And`, `Or` e `Not` também são aceitos com outras
  combinações de maiúsculas e minúsculas, mas recomendamos a grafia deste manual.

### 2.3 Eventos

Um evento diz **quando** um grupo de regras é avaliado.

| Evento | Quando executa |
|---|---|
| `onInit` | Uma vez, antes do primeiro ciclo (ao carregar a simulação e ao clicar em Encerrar). |
| `onStep` | Em todo ciclo. |
| `onTouch` | Em todo ciclo em que há pelo menos uma vizinha encostada, em qualquer face. |
| `onTouch(DIR)` | Em todo ciclo em que há uma vizinha encostada na face `DIR`. |
| `onMessage` | Uma vez para cada mensagem recebida no ciclo. |
| `onMessage(DIR)` | Uma vez para cada mensagem que veio do lado `DIR`. |

Um mesmo tipo pode ter vários `onTouch(...)` e `onMessage(...)` com direções diferentes. Todos os
que se aplicam são executados. Dentro do ciclo, a ordem é `onMessage`, depois `onTouch`, depois
`onStep` (veja a [seção 1.4](#14-o-que-acontece-dentro-de-um-ciclo)). Durante o `onInit`, os
eventos `onTouch` e `onMessage` não são avaliados.

#### onInit

Prepara a partícula: cor inicial, variáveis e mensagens iniciais. Aceita `setState`, `setColor`,
`sendMessage` e `wait`, além de condições. **Não aceita `move`**: isso é um erro de carregamento.
Mensagens enviadas aqui chegam no ciclo 1.

```json
{
  "onInit": [
    { "actions": ["setColor('#3399FF')", "setState('energia', 10)"] }
  ]
}
```

#### onStep

Roda em todo ciclo. É onde normalmente fica a lógica principal.

```json
{
  "onStep": [
    { "condition": "canMove('Y-')", "actions": ["move('Y-')"] }
  ]
}
```

#### onTouch e onTouch(DIR)

`onTouch` roda em todo ciclo em que alguma face tem vizinha; `onTouch(DIR)`, só quando a face
indicada tem vizinha. As regras rodam **uma vez por ciclo**, não uma vez por vizinha, e continuam
rodando enquanto o contato durar. A direção pode vir com ou sem aspas: `onTouch(Y-)` ou
`onTouch('Y-')`.

```json
{
  "onTouch": [
    { "actions": ["setColor('#FF0000')"] }
  ],
  "onTouch(Y-)": [
    { "actions": ["setState('apoiada', true)"] }
  ]
}
```

#### onMessage e onMessage(DIR)

`onMessage` roda **uma vez para cada mensagem** recebida no ciclo. `onMessage(DIR)` só considera as
mensagens que vieram do lado `DIR` (o lado onde a remetente estava). Dentro desses eventos,
`checkMessage` e `messageDirection()` se referem à mensagem da vez.

```json
{
  "onMessage(X-)": [
    {
      "condition": "checkMessage('aviso')",
      "actions": ["setColor('#FFCC00')"]
    }
  ]
}
```

### 2.4 Condições

A condição de uma regra é um texto que resulta em verdadeiro ou falso. As condições abaixo podem
ser combinadas com `And`, `Or` e `Not` (veja a [seção 2.5](#25-operadores-lógicos)).

#### hasNeighbor(dir)

Verdadeira se a célula vizinha na direção `dir` está ocupada.

```json
{ "condition": "hasNeighbor('Y-')", "actions": ["setColor('#FF6600')"] }
```

#### canMove(dir)

Verdadeira se a célula na direção `dir` está dentro da grade e livre (no início do ciclo). É só
uma consulta: não reserva a célula. Duas partículas podem ver `canMove` verdadeiro para a mesma
célula e colidir ao tentar entrar nela.

```json
{ "condition": "canMove('X+')", "actions": ["move('X+')"] }
```

#### checkState(chave, operador, valor)

Compara a variável `chave` da partícula com um `valor`, usando um operador de comparação (veja a
[seção 2.6](#26-operadores-de-comparação)). O valor pode ser um número, um texto, `true`, `false`
ou uma função de valor (veja a [seção 2.8](#28-funções-de-valor)).

```json
{ "condition": "checkState('energia', '>', 0)", "actions": ["setState('energia', -1)"] }
```

Se a variável **ainda não existe**, a comparação com `==` é falsa, com `!=` é verdadeira e com `>`,
`<`, `>=` e `<=` é falsa. Para evitar surpresas, dê um valor inicial às variáveis no `onInit`.

#### checkMessage(etiqueta)

Verdadeira se a partícula recebeu uma mensagem com essa etiqueta. Dentro de `onMessage`, olha só a
mensagem da vez. Em `onStep`, `onTouch` e `onInit`, olha todas as mensagens recebidas no ciclo. A
etiqueta é um texto ou número escrito diretamente (não pode ser uma função).

Além de responder verdadeiro ou falso, `checkMessage` **marca** a mensagem encontrada, para que
`messageValue()` possa ler a carga dela nas ações da mesma regra.

```json
{ "condition": "checkMessage('aviso')", "actions": ["setColor('#FFCC00')"] }
```

#### true e false

Valem, respectivamente, sempre e nunca. `false` é útil para desligar uma regra sem apagá-la.

```json
{ "condition": "false", "actions": ["move('X+')"] }
```

#### Nome de uma condição de definitions

Qualquer nome declarado em `definitions` pode ser usado como condição (veja a
[seção 2.11](#211-condições-com-nome-definitions)).

### 2.5 Operadores lógicos

| Operador | Significado |
|---|---|
| `A And B` | Verdadeira quando A e B são verdadeiras. |
| `A Or B` | Verdadeira quando pelo menos uma é verdadeira. |
| `Not A` | Inverte A. |

A precedência é: primeiro `Not`, depois `And`, depois `Or`. Assim, `Not a And b` significa
`(Not a) And b`, e `a Or b And c` significa `a Or (b And c)`.

```json
{
  "condition": "hasNeighbor('Y-') And Not checkState('pousou', '==', true)",
  "actions": ["setState('pousou', true)"]
}
```

**Não há parênteses para agrupar condições.** Para agrupar, crie uma condição com nome em
`definitions`. Escrever parênteses gera um erro:

```json
{
  "Exemplo": {
    "onStep": [
      { "condition": "(hasNeighbor('X+') Or hasNeighbor('X-')) And canMove('Y+')", "actions": ["move('Y+')"] }
    ]
  }
}
```

```text
• Comportamento: tipo 'Exemplo', evento 'onStep', bloco 1: condição "(hasNeighbor('X+') Or hasNeighbor('X-')) And canMove('Y+')" — esperava uma condição, encontrado '('.
```

### 2.6 Operadores de comparação

Usados em `checkState`. Podem ser escritos com ou sem aspas: `'>='` ou `>=`.

| Operador | Significado |
|---|---|
| `==` | igual |
| `!=` | diferente |
| `>` | maior |
| `<` | menor |
| `>=` | maior ou igual |
| `<=` | menor ou igual |

- Números são comparados como números (com uma tolerância de 0,000001 na igualdade).
- Textos são comparados sem diferença de maiúsculas e minúsculas.
- `>`, `<`, `>=` e `<=`, quando o valor é escrito diretamente, só aceitam números. Comparar com um
  texto escrito diretamente é um erro de carregamento.

```json
{ "condition": "checkState('modo', '!=', 'parado') And checkState('passos', '<=', 5)", "actions": ["setState('passos', +1)"] }
```

### 2.7 Ações

#### move(dir)

Pede um movimento de uma célula na direção `dir`. O movimento acontece no fim do ciclo, se não
houver colisão. Só o primeiro `move` do ciclo vale. Não é permitido no `onInit`. Sair da grade é
movimento inválido.

```json
{ "condition": "canMove('X+')", "actions": ["move('X+')"] }
```

#### setState(chave, valor)

Grava um valor na variável `chave` da partícula, criando-a se necessário. O valor pode ser um
número, um texto, `true`, `false` ou uma função de valor (por exemplo, `state('outra')`, `myId()`
ou `canMove('X+')`). Com um sinal `+` ou `-` antes de um número ou de certas funções, a operação é
**relativa** (soma ou subtrai; veja a [seção 2.10](#210-operação-relativa-no-setstate)).

```json
{ "actions": ["setState('modo', 'explorando')", "setState('vizinhas', countNeighbors())"] }
```

#### setColor('#RRGGBB')

Muda a cor da partícula. A cor é escrita em hexadecimal, entre aspas: `'#FF6600'` (vermelho,
verde e azul, de `00` a `FF` cada). Também é aceita a forma curta `'#F60'`, equivalente a
`'#FF6600'`, e as formas com quatro e oito dígitos (`'#RGBA'` e `'#RRGGBBAA'`). Partículas que
nunca recebem `setColor` usam a cor escolhida em "Cor das partículas", na interface.

```json
{ "actions": ["setColor('#33CC66')"] }
```

#### wait

Não faz nada (pode ser escrita `wait` ou `wait()`). Não conta como atividade. É útil quando uma
regra precisa existir mas não deve agir, já que a lista de ações não pode ser vazia.

```json
{ "condition": "hasNeighbor('Y-')", "actions": ["wait"] }
```

#### sendMessage

Envia uma mensagem. Há quatro formas:

| Forma | Destino | Carga |
|---|---|---|
| `sendMessage(etiqueta)` | todas as vizinhas | não |
| `sendMessage(dir, etiqueta)` | só a vizinha da face `dir` | não |
| `sendMessage(etiqueta, carga)` | todas as vizinhas | sim |
| `sendMessage(dir, etiqueta, carga)` | só a vizinha da face `dir` | sim |

- A **etiqueta** é um texto ou número escrito diretamente.
- A **carga** é um número, escrito diretamente ou calculado por uma função de valor (por exemplo,
  `myId()` ou `state('energia')`). A carga só transporta números: se a função produzir texto ou
  verdadeiro ou falso, a mensagem segue **sem carga**.
- Para todas as vizinhas, o simulador envia uma mensagem para cada vizinha encostada. Cada uma
  conta em "Mensagens enviadas".
- Se não houver vizinha na direção (ou nenhuma vizinha, no envio para todas), ninguém recebe, mas a
  tentativa conta uma vez em "Mensagens enviadas". Só conta como atividade o envio que chega a
  alguém.
- Com **dois argumentos**, o simulador decide a forma pelo primeiro: se ele for uma direção (ou
  `state(...)`, `opposite(...)` ou `messageDirection()`), é `sendMessage(dir, etiqueta)`. Portanto
  `sendMessage('X+', 5)` envia a etiqueta 5 só para a vizinha `X+`, e não a etiqueta `'X+'` com
  carga 5 para todas.

```json
{
  "actions": [
    "sendMessage('ola')",
    "sendMessage('X+', 'aviso')",
    "sendMessage('id', myId())",
    "sendMessage('Y+', 'energia', state('energia'))"
  ]
}
```

### 2.8 Funções de valor

As funções de valor produzem um valor que pode ser usado no lugar de um valor escrito diretamente:
no valor do `checkState`, no valor do `setState` e na carga do `sendMessage`. As que produzem
direções também podem ser usadas onde se espera uma direção.

#### state(chave)

O valor atual da variável `chave`. Se a variável não existe, o resultado é "nada" (veja a regra
das comparações em [checkState](#checkstatechave-operador-valor)).

```json
{ "condition": "checkState('energia', '<', state('limite'))", "actions": ["setState('energia', +1)"] }
```

#### opposite(dir)

A direção oposta: `opposite('X+')` é `'X-'`. Aceita também outra função que produza direção, como
`opposite(state('rumo'))`.

```json
{ "condition": "canMove(opposite(state('rumo')))", "actions": ["move(opposite(state('rumo')))"] }
```

#### myId()

O id da partícula, como número.

```json
{ "actions": ["setState('meuNumero', myId())", "sendMessage('id', myId())"] }
```

#### countNeighbors()

Quantas das seis faces têm vizinha encostada, de 0 a 6.

```json
{ "condition": "hasNeighbor('Y-')", "actions": ["setState('vizinhas', countNeighbors())"] }
```

#### canMove(dir) e hasNeighbor(dir) como valor

Produzem verdadeiro ou falso, exatamente como as condições de mesmo nome.

```json
{ "actions": ["setState('livreAcima', canMove('Y+'))", "setState('apoiada', hasNeighbor('Y-'))"] }
```

#### checkMessage(etiqueta) como valor

Produz verdadeiro ou falso e também marca a mensagem encontrada, como a condição de mesmo nome.

```json
{ "actions": ["setState('recebeuAviso', checkMessage('aviso'))"] }
```

#### messageValue()

A carga numérica da mensagem que um `checkMessage` **na condição da mesma regra** encontrou. Sem um
`checkMessage` que tenha encontrado mensagem, ou se a mensagem veio sem carga, o resultado é
"nada". Funciona dentro de `onMessage` e também em `onStep` e `onTouch`, desde que haja o
`checkMessage` na condição.

```json
{ "condition": "checkMessage('energia') And checkState('energia', '<', messageValue())", "actions": ["setState('energia', messageValue())"] }
```

#### messageDirection()

O lado de onde veio a mensagem da vez, visto pela partícula que recebeu: `'X+'`, `'X-'`, `'Y+'`,
`'Y-'`, `'Z+'` ou `'Z-'`. Não precisa de `checkMessage`. **Só pode ser usada dentro de
`onMessage`** (na condição ou nas ações); em qualquer outro evento é um erro de carregamento. É
útil para responder a quem enviou:

```json
{
  "onMessage": [
    { "condition": "checkMessage('ping')", "actions": ["sendMessage(messageDirection(), 'pong')"] }
  ]
}
```

Atenção à diferença: `messageValue()` lê a mensagem que o `checkMessage` da condição encontrou,
enquanto `messageDirection()` lê a mensagem da vez do `onMessage`. Quando os dois são usados numa
regra de `onMessage` com `checkMessage` na condição, descrevem a mesma mensagem.

#### moveResult()

O que aconteceu com o movimento da partícula **no ciclo anterior**:

| Valor | Significado |
|---|---|
| `'moved'` | moveu-se |
| `'blockedByCollision'` | tentou e foi bloqueada por colisão |
| `'invalid'` | tentou um movimento inválido (fora da grade ou direção inexistente) |
| `'notAttempted'` | não tentou se mover (também é o valor no primeiro ciclo) |

Para usar o resultado numa condição, grave-o antes numa variável, numa regra anterior. Como o
`setState` vale na hora, a regra seguinte já enxerga o valor:

```json
{
  "onStep": [
    { "actions": ["setState('ultimo', moveResult())"] },
    { "condition": "checkState('ultimo', '==', 'blockedByCollision')", "actions": ["setColor('#FF0000')"] }
  ]
}
```

Lembre que um `setState` em todo ciclo conta como atividade e impede o encerramento por
inatividade.

### 2.9 Direções

| Direção | Sentido |
|---|---|
| `X+` e `X-` | eixo X (horizontal) |
| `Y+` e `Y-` | eixo Y (vertical): `Y+` para cima, `Y-` para baixo |
| `Z+` e `Z-` | eixo Z (horizontal) |

- Podem ser escritas com ou sem aspas (`'X+'` ou `X+`) e com letra minúscula (`x+`).
- Onde se espera uma direção, também se pode usar `state('chave')` (uma variável que guarda uma
  direção), `opposite(...)` ou, dentro de `onMessage`, `messageDirection()`.
- Se esse valor não for uma direção válida no momento da execução, o `move` vira movimento
  inválido, `hasNeighbor` e `canMove` dão falso, e o `sendMessage` não chega a ninguém (mas conta
  como enviado).

```json
{
  "onInit": [
    { "actions": ["setState('rumo', 'Z+')"] }
  ],
  "onStep": [
    { "condition": "canMove(state('rumo'))", "actions": ["move(state('rumo'))"] }
  ]
}
```

### 2.10 Operação relativa no setState

Um sinal `+` ou `-` antes do valor transforma o `setState` numa soma ou subtração sobre o valor
atual:

| Escrita | Efeito |
|---|---|
| `setState('x', +1)` | x = x + 1 |
| `setState('x', -2.5)` | x = x - 2,5 |
| `setState('s', +state('n'))` | s = s + n |
| `setState('s', -state('n'))` | s = s - n |
| `setState('d', +messageValue())` | d = d + carga da mensagem |
| `setState('t', +myId())` | t = t + id |
| `setState('c', +countNeighbors())` | c = c + número de vizinhas |

- Se a variável ainda não existe, a conta parte de 0.
- Se a variável guarda um texto ou verdadeiro ou falso, a operação é ignorada: a variável não muda
  e isso não conta como atividade.
- Se a função depois do sinal não produzir um número (variável inexistente, `messageValue()` sem
  carga), a operação também é ignorada.
- O sinal só é aceito antes de um número ou de `state`, `messageValue`, `myId` e `countNeighbors`, e
  só no valor do `setState`. Em qualquer outro lugar é um erro de carregamento.
- **Não há outras contas**: não existem `a + b`, multiplicação nem divisão. Para somar duas
  variáveis, use dois passos: `setState('soma', state('a'))` e depois `setState('soma', +state('b'))`.
- Não é possível gravar diretamente um número negativo: `setState('x', -3)` subtrai 3. Para gravar
  -3, zere antes: `setState('x', 0)` e depois `setState('x', -3)`.
- Entre aspas, o sinal não é relativo: `setState('x', '+5')` grava 5.

```json
{ "actions": ["setState('passos', +1)", "setState('total', +state('passos'))"] }
```

### 2.11 Condições com nome (definitions)

A chave `definitions`, no primeiro nível do arquivo, dá nome a condições. O nome pode então ser
usado como condição em qualquer regra de qualquer tipo, combinado com `And`, `Or` e `Not`.

- Uma condição com nome se comporta como se estivesse entre parênteses. Por isso, é a forma de
  agrupar condições.
- Uma definição pode usar outra, mas não pode haver referência circular (A usa B, que usa A).
- Os nomes diferenciam maiúsculas de minúsculas.
- Uma definição que usa `messageDirection()` só pode ser usada em regras de `onMessage`.
- Os nomes só valem como condição; não podem ser usados como valor dentro de funções.

```json
{
  "definitions": {
    "ladoOcupado": "hasNeighbor('X+') Or hasNeighbor('X-')",
    "podeSubir": "ladoOcupado And canMove('Y+')"
  },
  "Escalador": {
    "onStep": [
      { "condition": "podeSubir", "actions": ["move('Y+')"] },
      { "condition": "Not ladoOcupado", "actions": ["setColor('#999999')"] }
    ]
  }
}
```

Aqui, `podeSubir` equivale a `(hasNeighbor('X+') Or hasNeighbor('X-')) And canMove('Y+')`, algo que
não dá para escrever diretamente, por falta de parênteses.

### 2.12 Mensagens de erro comuns

Quando um arquivo tem erro, nada é carregado e os problemas aparecem na caixa vermelha logo abaixo
do cartão do arquivo (cenário ou comportamento). Cada linha começa com `Cenário:` ou
`Comportamento:` e diz onde está o problema: o tipo, o evento, o número do bloco (regra) e o
número da ação, contando a partir de 1. São mostrados até 10 erros por vez; se houver mais, a
última linha diz `... e mais N erro(s).`. Depois de corrigir o arquivo, clique de novo em
**Carregar simulação**: os dois arquivos são lidos outra vez.

A seguir, os erros mais comuns, como aparecem na tela.

#### JSON mal formado

Uma vírgula, aspas ou chave faltando ou sobrando. A mensagem começa assim e continua com uma
descrição técnica, em inglês, que indica a linha (`line`) e a posição (`position`) do problema:

```json
{
  "Andarilho": {
    "onStep": [
      { "actions": ["move('X+')"] }
    ]
  }
  "Marco": {}
}
```

```text
• Comportamento: JSON inválido —
```

Correção: confira vírgulas entre os itens, aspas e chaves. Um editor como o Visual Studio Code
destaca esses erros.

#### Campo desconhecido numa regra

Normalmente, erro de digitação em `condition` ou `actions`:

```json
{
  "Andarilho": {
    "onStep": [
      { "conditon": "canMove('X+')", "actions": ["move('X+')"] }
    ]
  }
}
```

```text
• Comportamento: tipo 'Andarilho', evento 'onStep', bloco 1: campo desconhecido 'conditon' (um bloco aceita apenas 'condition' e 'actions').
```

#### Campo com maiúsculas e minúsculas trocadas

Os nomes dos campos têm uma grafia exata. Escrito com outra combinação de maiúsculas e minúsculas,
o campo é recusado, e a mensagem indica a grafia correta:

```json
{
  "Andarilho": {
    "onStep": [
      { "Condition": "canMove('X+')", "actions": ["move('X+')"] }
    ]
  }
}
```

```text
• Comportamento: tipo 'Andarilho', evento 'onStep', bloco 1: campo "Condition" não reconhecido. Use "condition".
```

O mesmo vale para `actions` e `definitions` e para todos os campos do arquivo de cenário (veja os
erros do cenário, mais abaixo).

#### Regra sem ações

```json
{
  "Andarilho": {
    "onStep": [
      { "condition": "canMove('X+')", "actions": [] }
    ]
  }
}
```

```text
• Comportamento: tipo 'Andarilho', evento 'onStep', bloco 1: 'actions' não pode ser uma lista vazia.
```

Correção: escreva ao menos uma ação. Se a regra não deve fazer nada, use `wait`.

#### Evento desconhecido

```json
{
  "Andarilho": {
    "onStart": [
      { "actions": ["setColor('#3399FF')"] }
    ]
  }
}
```

```text
• Comportamento: tipo 'Andarilho', evento 'onStart': chave de evento desconhecida (esperado: onInit, onStep, onTouch, onTouch(DIR), onMessage, onMessage(DIR)).
```

#### Função desconhecida

Erro de digitação no nome de uma condição, ação ou função:

```json
{
  "Andarilho": {
    "onStep": [
      { "condition": "canMov('X+')", "actions": ["move('X+')"] }
    ]
  }
}
```

```text
• Comportamento: tipo 'Andarilho', evento 'onStep', bloco 1: condição "canMov('X+')" — função desconhecida 'canMov' (reconhecidas em condição: canMove, checkMessage, checkState, hasNeighbor).
```

#### Número errado de argumentos

Um caso frequente é usar vírgula como separador decimal, o que cria um argumento a mais:

```json
{
  "Andarilho": {
    "onInit": [
      { "actions": ["setState('peso', 2,5)"] }
    ]
  }
}
```

```text
• Comportamento: tipo 'Andarilho', evento 'onInit', bloco 1, ação 1: ação "setState('peso', 2,5)" — 'setState' espera 2 argumento(s), mas recebeu 3.
```

Correção: `setState('peso', 2.5)`.

#### Direção inválida

```json
{
  "Andarilho": {
    "onStep": [
      { "actions": ["move(direita)"] }
    ]
  }
}
```

```text
• Comportamento: tipo 'Andarilho', evento 'onStep', bloco 1, ação 1: ação "move(direita)" — argumento 1 de 'move' deve ser uma direção válida (X+, X-, Y+, Y-, Z+, Z-), mas é 'direita'.
```

#### Comparação de grandeza com texto

```json
{
  "Andarilho": {
    "onStep": [
      { "condition": "checkState('nivel', '>', alto)", "actions": ["wait"] }
    ]
  }
}
```

```text
• Comportamento: tipo 'Andarilho', evento 'onStep', bloco 1: condição "checkState('nivel', '>', alto)" — o operador '>' em 'checkState' só compara valores numéricos, mas 'alto' é um literal booleano ou de texto.
```

Correção: compare com um número, ou use `==` e `!=` para textos.

#### Cor fora do formato

```json
{
  "Andarilho": {
    "onInit": [
      { "actions": ["setColor(laranja)"] }
    ]
  }
}
```

```text
• Comportamento: tipo 'Andarilho', evento 'onInit', bloco 1, ação 1: ação "setColor(laranja)" — argumento 1 de 'setColor' deve ser uma cor hexadecimal entre aspas no formato '#RRGGBB', mas é 'laranja'.
```

Correção: `setColor('#FF8800')`.

#### Movimento no onInit

```json
{
  "Andarilho": {
    "onInit": [
      { "actions": ["move('X+')"] }
    ]
  }
}
```

```text
• Comportamento: tipo 'Andarilho', evento 'onInit', bloco 1, ação 1: ação "move('X+')" — ações de movimento não são permitidas em onInit.
```

Correção: mova a regra para `onStep`.

#### messageDirection() fora de onMessage

```json
{
  "Andarilho": {
    "onStep": [
      { "actions": ["sendMessage(messageDirection(), 'ok')"] }
    ]
  }
}
```

```text
• Comportamento: tipo 'Andarilho', evento 'onStep', bloco 1, ação 1: ação "sendMessage(messageDirection(), 'ok')" — messageDirection() só pode ser usada em blocos onMessage, onde existe uma mensagem recebida.
```

#### Aspas não fechadas

```json
{
  "Andarilho": {
    "onInit": [
      { "actions": ["setColor('#3399FF)"] }
    ]
  }
}
```

```text
• Comportamento: tipo 'Andarilho', evento 'onInit', bloco 1, ação 1: ação "setColor('#3399FF)" — literal de texto não terminado a partir da posição 9.
```

#### Referência circular em definitions

```json
{
  "definitions": {
    "a": "b And canMove('X+')",
    "b": "a Or hasNeighbor('Y-')"
  },
  "Andarilho": {
    "onStep": [
      { "condition": "a", "actions": ["move('X+')"] }
    ]
  }
}
```

```text
• Comportamento: 'definitions' tem uma referência circular envolvendo 'a'.
```

#### Erros do arquivo de cenário

Posição fora da grade:

```json
{
  "gridSize": [8, 2, 1],
  "particles": [
    { "id": 1, "type": "Andarilho", "position": [8, 0, 0] }
  ]
}
```

```text
• Cenário: partícula id 1: 'position' eixo x = 8 está fora da grade (válido: 0 a 7).
```

Duas partículas na mesma célula:

```json
{
  "gridSize": [8, 2, 1],
  "particles": [
    { "id": 1, "type": "Andarilho", "position": [0, 0, 0] },
    { "id": 2, "type": "Andarilho", "position": [0, 0, 0] }
  ]
}
```

```text
• Cenário: partícula id 2: mais de uma partícula começa na posição [0, 0, 0].
```

Id repetido:

```json
{
  "gridSize": [8, 2, 1],
  "particles": [
    { "id": 1, "type": "Andarilho", "position": [0, 0, 0] },
    { "id": 1, "type": "Andarilho", "position": [1, 0, 0] }
  ]
}
```

```text
• Cenário: id de partícula duplicado: 1.
```

Tamanho da grade ausente:

```json
{
  "particles": [
    { "id": 1, "type": "Andarilho", "position": [0, 0, 0] }
  ]
}
```

```text
• Cenário: 'gridSize' é obrigatório e deve ser uma lista [x, y, z].
```

Campo com maiúsculas e minúsculas trocadas:

```json
{
  "gridSize": [8, 2, 1],
  "Particles": [
    { "id": 1, "type": "Andarilho", "position": [0, 0, 0] }
  ]
}
```

```text
• Cenário: campo "Particles" não reconhecido. Use "particles".
```

Dentro de uma partícula, a mensagem diz qual é a partícula, por exemplo
`Cenário: partícula id 1: campo "Type" não reconhecido. Use "type".`.

#### Tipo que não existe no comportamento

Se o cenário usa um tipo que o arquivo de comportamento não define, o erro aparece no cartão do
**cenário**. Quando a diferença é só de maiúsculas e minúsculas, a mensagem avisa. Por exemplo,
com uma partícula do tipo `andarilho` no cenário e o tipo `Andarilho` no comportamento:

```text
• Cenário: tipo de partícula 'andarilho' (partícula id 1) não existe no arquivo de comportamento — o comportamento define 'Andarilho', e a comparação diferencia maiúsculas de minúsculas.
```

#### Arquivo não encontrado

Se o arquivo foi apagado, movido ou renomeado depois de escolhido, aparece
`Cenário: arquivo não encontrado em '...'` (ou `Comportamento: ...`), com o caminho do arquivo.
Escolha o arquivo de novo com **+ Carregar arquivo**.

---

## 3. Arquivo de cenário

O arquivo de cenário descreve a grade e a situação inicial: onde está cada partícula e de que tipo
ela é.

```json
{
  "gridSize": [10, 5, 10],
  "maxCycles": 100,
  "idleCyclesLimit": 5,
  "particles": [
    { "id": 1, "type": "Queda", "position": [4, 4, 4] },
    { "id": 2, "type": "Chao", "position": [4, 0, 4] }
  ]
}
```

### 3.1 Campos

| Campo | Obrigatório | Valor | Padrão |
|---|---|---|---|
| `gridSize` | sim | Lista de 3 números inteiros maiores que zero: `[x, y, z]`. | não há |
| `maxCycles` | não | Número inteiro. Encerra a simulação ao concluir esse número de ciclos. 0 ou negativo: sem limite. | 0 (sem limite) |
| `idleCyclesLimit` | não | Número inteiro. Encerra depois desse número de ciclos seguidos sem atividade (veja a [seção 1.7](#17-quando-a-simulação-termina)). 0 ou negativo: desligado. | 5 |
| `particles` | não | Lista de partículas. Pode ser vazia. | lista vazia |

Cada item de `particles`:

| Campo | Obrigatório | Valor |
|---|---|---|
| `id` | sim | Número inteiro, sem repetição. |
| `type` | sim | Texto não vazio, igual (com as mesmas maiúsculas e minúsculas) a um tipo do arquivo de comportamento. |
| `position` | sim | Lista de 3 números inteiros `[x, y, z]`, cada um de 0 até o tamanho da grade menos 1. |

Observações:

- Escreva os nomes dos campos exatamente como na tabela. Com outra combinação de maiúsculas e
  minúsculas (`Particles`, `MaxCycles`, `Type` etc.), o arquivo é recusado.
- Campos com outros nomes, que não estão na tabela, são ignorados.
- A ordem das partículas na lista não muda movimentos nem colisões. Ela só define a ordem em que
  mensagens chegadas no mesmo ciclo são tratadas (veja a [seção 1.5](#15-mensagens)).
- Como `idleCyclesLimit` vale 5 quando não é informado, um cenário em que nada acontece encerra
  sozinho depois de 5 ciclos.

### 3.2 Validações

Ao carregar, o simulador verifica:

- nomes de campos com a grafia exata da tabela;
- `gridSize` presente, com 3 números inteiros maiores que zero;
- `maxCycles` e `idleCyclesLimit`, se presentes, números inteiros;
- cada partícula com `id`, `type` e `position` presentes e no formato certo;
- nenhum `id` repetido;
- cada posição dentro da grade;
- nenhuma célula com mais de uma partícula no início;
- cada `type` definido no arquivo de comportamento.

As mensagens de erro correspondentes estão na [seção 2.12](#212-mensagens-de-erro-comuns).

---

## 4. Tutorial da interface

### 4.1 Passo a passo com o exemplo 1

Este tutorial usa o [exemplo 1](#51-exemplo-1-até-a-borda): uma partícula azul (`Andarilho`) anda
para a direita até a borda da grade e fica laranja, e uma partícula cinza (`Marco`) fica parada
para marcar o fim do caminho. Os arquivos estão na pasta `exemplos` do pacote do simulador e em
[`docs/exemplos`](exemplos).

**Passo 1: abrir o simulador.** Abra `Programmable Matter Simulator.exe`. A tela tem:

- ao centro, a vista 3D da grade;
- no alto, ao centro, uma barra flutuante com os botões de controle;
- à esquerda, o painel **INDICADORES**;
- à direita, o **PAINEL DE CONFIGURAÇÃO**.

Ao abrir, o simulador já carrega uma demonstração (blocos que caem sobre um piso), e a linha de
status mostra `Simulação carregada (padrão).`. Ela fica parada até você clicar em Iniciar.

**Passo 2: escolher o arquivo de cenário.** No cartão **CONFIGURAÇÃO DO CENÁRIO**, clique em
**+ Carregar arquivo**. Na janela "Selecionar arquivo de cenário (JSON)" (que lista os tipos
"JSON files" e "All files"), escolha `cenario_borda.json`. O nome aparece em "Arquivo ativo".

**Passo 3: escolher o arquivo de comportamento.** No cartão **COMPORTAMENTO DAS PARTÍCULAS**,
faça o mesmo e escolha `comportamento_borda.json`.

**Passo 4: carregar a simulação.** Clique em **Carregar simulação**. A linha de status mostra
`Simulação carregada. Pressione "Iniciar".`. Na vista 3D aparecem o cubo azul à esquerda e o cubo
cinza no fim do caminho, uma célula acima. O painel de indicadores mostra `Partículas: 2` e os
contadores zerados (o FPS continua mostrando a taxa de quadros atual). Se algum arquivo tiver
erro, o problema aparece na caixa vermelha do
cartão correspondente (veja a [seção 2.12](#212-mensagens-de-erro-comuns)).

**Passo 5 (opcional): ajustar a velocidade.** No cartão **SIMULAÇÃO**, o controle "Intervalo
entre ciclos" define quanto tempo passa entre um ciclo e outro. O padrão é 0,25 s. Para
acompanhar com calma, use 0,5 s ou 1 s. Isso muda só a velocidade, nunca o resultado.

**Passo 6: iniciar.** Clique em **Iniciar** (o triângulo da barra do alto). A linha de status
mostra `Rodando...`. A cada ciclo, o cubo azul anda uma célula. Acompanhe no painel de
indicadores: "Ciclos" e "Movimentos" sobem juntos, e "Tempo decorrido" conta os segundos.

**Passo 7 (opcional): pausar.** **Pausar** (as duas barras) congela a simulação e o tempo;
**Iniciar** continua do ponto em que parou.

**Passo 8: o fim.** No ciclo 8, o cubo azul não consegue mais avançar (está na última coluna) e
fica laranja. Nos ciclos 9, 10 e 11 nada acontece, e a simulação encerra sozinha. A linha de
status mostra:

```text
Simulação encerrada: sem atividade por 3 ciclo(s).
```

**Passo 9: interpretar os indicadores.** Ao final, o painel mostra:

| Indicador | Valor | Por quê |
|---|---|---|
| Partículas | 2 | o Andarilho e o Marco |
| Ciclos | 11 | 7 ciclos andando, 1 mudando de cor e 3 sem atividade (o `idleCyclesLimit` do cenário) |
| Movimentos | 7 | da coluna 0 até a coluna 7 |
| Ciclo do último movimento | 7 | o último passo aconteceu no ciclo 7 |
| Colisões | 0 | nada bloqueou o caminho |
| Movimentos inválidos | 0 | a regra só pede movimento quando `canMove('X+')` é verdadeiro |
| Mensagens enviadas e recebidas | 0 | o exemplo não usa mensagens |
| Tempo decorrido | cerca de 2,75 s | 11 ciclos de 0,25 s (com o intervalo padrão) |
| FPS, FPS médio e Tempo por ciclo | variam | dependem do computador |

A [seção 4.3](#43-painel-de-indicadores) explica cada indicador.

**Passo 10: exportar os indicadores.** Clique em **Baixar indicadores**, no rodapé do painel de
indicadores. Na janela "Baixar indicadores (CSV)", o nome sugerido é
`indicadores_cenario_borda_comportamento_borda_<data e hora>.csv`. Escolha a pasta e clique em
Salvar. A linha de status mostra `Indicadores gravados em <caminho do arquivo>`. Abra o arquivo
numa planilha (por exemplo, o Excel): ele tem uma linha de cabeçalho e uma linha com os valores
deste momento. Se a data aparecer como `####`, alargue as colunas (selecione a planilha inteira e
dê um clique duplo numa divisa entre as letras das colunas).

**Passo 11: repetir.** Clique em **Encerrar** (o quadrado) para voltar ao início: as partículas
voltam às posições iniciais, as variáveis são apagadas, o `onInit` roda de novo e os indicadores
zeram. A linha de status mostra `Resetado para o início.`. Clique em **Iniciar** para rodar de novo.
Se exportar outra vez escolhendo o mesmo arquivo CSV, uma nova linha é acrescentada a ele.

### 4.2 Referência dos controles

#### Barra de controles (no alto, ao centro)

| Botão | Dica na tela | O que faz |
|---|---|---|
| Triângulo | Iniciar | Inicia ou continua a simulação. Depois que ela encerra sozinha, não continua: use Encerrar. |
| Duas barras | Pausar | Pausa. O tempo e a contagem de FPS médio também param. |
| Quadrado | Encerrar | Volta ao ciclo 0: posições iniciais, variáveis apagadas, mensagens pendentes descartadas, `onInit` executado de novo e indicadores zerados. Não relê os arquivos. A simulação fica parada até Iniciar. |
| Cantos | Ampliar / recolher painel lateral | Esconde ou mostra o painel de configuração, para ampliar a vista 3D. |
| Câmera | Focar câmera nas partículas (F) | Reposiciona a câmera para enquadrar todas as partículas. Atalho: tecla F. |

#### Painel de configuração (à direita)

No cabeçalho:

| Botão | Dica na tela | O que faz |
|---|---|---|
| Tela cheia | Tela Cheia | Alterna entre janela e tela cheia. |
| Sair | Sair | Pergunta "Deseja realmente sair?" e fecha o simulador se você confirmar. |

Cartões **CONFIGURAÇÃO DO CENÁRIO** e **COMPORTAMENTO DAS PARTÍCULAS**:

| Controle | O que faz |
|---|---|
| Arquivo ativo | Nome do arquivo escolhido. Ao abrir o simulador, mostra os arquivos da demonstração (`scenario.json` e `behavior.json`). |
| ✕ (dica: Remover arquivo) | Esvazia o campo, que passa a mostrar `(nenhum arquivo)`. A simulação já carregada continua carregada. |
| + Carregar arquivo | Abre a janela para escolher um arquivo JSON. |
| Caixa vermelha | Aparece só quando há erro, com as mensagens do arquivo daquele cartão. |

Botão **Carregar simulação**: lê do disco os dois arquivos escolhidos, verifica, monta a
simulação, executa o `onInit` e zera os indicadores. Não inicia: clique em Iniciar. Você pode
editar um arquivo e clicar de novo para recarregá-lo. Se um dos campos estiver vazio, aparece no
cartão `Nenhum arquivo de cenário selecionado. Use "+ Carregar arquivo" para escolher um.` (ou a
mensagem equivalente para o comportamento). Se a carga falhar, a simulação anterior continua
carregada.

**Linha de status** (abaixo do botão Carregar simulação). Mensagens possíveis:

| Mensagem | Quando aparece |
|---|---|
| `Simulação carregada (padrão).` | ao abrir, com a demonstração carregada |
| `Falha ao carregar padrões. Verifique os erros abaixo.` | ao abrir, se a demonstração não carregar |
| `Simulação carregada. Pressione "Iniciar".` | depois de Carregar simulação |
| `Falha ao carregar. Verifique os erros abaixo.` | Carregar simulação encontrou erros |
| `Falta selecionar arquivo. Verifique os erros abaixo.` | Carregar simulação com um campo vazio |
| `Rodando...` | depois de Iniciar |
| `Pausado.` | depois de Pausar |
| `Resetado para o início.` | depois de Encerrar |
| `Simulação encerrada: <motivo>.` | quando a simulação encerra sozinha (o motivo está na [seção 1.7](#17-quando-a-simulação-termina)) |
| `Simulação encerrada. Use "Encerrar" para resetar.` | Iniciar depois que a simulação encerrou |
| `Câmera reenquadrada.` | depois de Focar câmera nas partículas |
| `Indicadores gravados em <caminho>` | exportação concluída |
| `Não foi possível gravar os indicadores: <motivo>` | exportação falhou |

Depois que a simulação encerra, Pausar não muda a mensagem de encerramento.

Cartão **SIMULAÇÃO**:

| Controle | Faixa | Padrão | O que faz |
|---|---|---|---|
| Intervalo entre ciclos | 0 a 2 s | 0,25 s | Tempo entre um ciclo e o próximo. Em 0, o simulador roda um ciclo por quadro desenhado e mostra `1 ciclo/quadro`. |

Cartão **VISUALIZAÇÃO**:

| Controle | Faixa | Padrão | O que faz |
|---|---|---|---|
| Preenchimento do cubo | 10% a 100% | 90% | Tamanho do cubo dentro da célula. |
| Duração da animação | 0 a 1 s | 0,25 s | Tempo da animação de cada passo. Em 0, mostra `instantâneo` e o cubo salta direto para a nova célula. |
| Cor das partículas | 25 cores | azul-claro | Cor das partículas que nunca receberam `setColor`. As que já receberam mantêm a sua. |
| Cor dos limites | 25 cores | azul translúcido | Cor do contorno da grade. |
| Mostrar limites da grade | ligado ou desligado | ligado | Mostra ou esconde o contorno da grade. |

Os valores dos controles deslizantes podem ser digitados: clique no número, digite (com vírgula
ou ponto) e tecle Enter. Nenhum desses controles muda o resultado da simulação; eles só mudam a
velocidade e a aparência.

#### Câmera

| Comando | Ação |
|---|---|
| W, A, S, D | Move a câmera para a frente, a esquerda, trás e direita, em relação à direção em que ela olha. |
| E e Q | Sobe e desce. |
| Botão direito do mouse (segurar e mover) | Gira a câmera para olhar em volta. |
| Roda do mouse | Aumenta ou diminui a velocidade da câmera. |
| Shift (segurar) | Acelera o movimento da câmera. |
| F | Enquadra todas as partículas. |

Ao carregar uma simulação, a câmera já enquadra as partículas e ajusta a sua velocidade ao
tamanho do cenário. As setas do teclado não movem a câmera: se um controle deslizante estiver
selecionado, elas alteram o valor dele.

### 4.3 Painel de indicadores

| Indicador | Significado |
|---|---|
| Partículas | Número de partículas do cenário carregado. |
| Movimentos | Total de movimentos realizados (uma partícula que andou uma célula num ciclo conta 1). Movimentos bloqueados ou inválidos não contam. |
| Colisões | Quantas vezes uma partícula tentou se mover e foi bloqueada (veja a [seção 1.6](#16-colisões-e-movimentos-inválidos)). Três partículas disputando a mesma célula contam 3. |
| Movimentos inválidos | Tentativas de sair da grade ou de mover numa direção inexistente. |
| FPS | Quadros desenhados por segundo neste momento. Continua sendo atualizado com a simulação pausada. |
| FPS médio | Quadros desenhados com a simulação rodando, divididos pelo "Tempo decorrido". Mostra um traço (—) antes de iniciar e fica congelado depois do encerramento. |
| Tempo decorrido | Segundos com a simulação rodando. Pausas não contam. |
| Ciclos | Ciclos concluídos. |
| Ciclo do último movimento | O último ciclo em que pelo menos uma partícula se moveu. Mostra um traço (—) enquanto nenhuma se moveu. |
| Tempo por ciclo | Tempo médio, em milissegundos, que o computador levou para calcular um ciclo. Não é o intervalo entre ciclos. |
| Mensagens enviadas | Tentativas de envio: uma para cada vizinha alcançada, mais uma para cada `sendMessage` que não alcançou ninguém. |
| Mensagens recebidas | Mensagens efetivamente entregues. Pode ser menor que "Mensagens enviadas". |

Os indicadores zeram ao carregar uma simulação e ao clicar em Encerrar.

Uma observação sobre "Tempo decorrido" e "FPS médio": cada quadro conta no máximo 0,333 s. Se o
computador travar por mais tempo que isso, o excesso não entra na conta.

### 4.4 Exportação dos indicadores (CSV)

O botão **Baixar indicadores** grava os valores do painel num arquivo CSV, que pode ser aberto em
planilhas como o Excel. Ele só fica disponível com uma simulação carregada.

- Os valores gravados são os do **momento do clique**.
- O nome sugerido é `indicadores_<cenário>_<comportamento>_<aaaa-MM-dd_HH-mm-ss>.csv`, com os nomes
  dos arquivos carregados (sem `.json`), a data e a hora.
- **Arquivo novo:** recebe o cabeçalho e uma linha de valores.
- **Arquivo já existente, criado pelo simulador:** recebe mais uma linha. Assim, você pode juntar
  várias execuções numa só tabela. A janela não pergunta se quer substituir o arquivo, porque ele
  não é substituído.
- **Arquivo existente com outro conteúdo:** é recusado e fica intacto. A linha de status mostra
  `Não foi possível gravar os indicadores: o arquivo escolhido já existe e não é uma tabela de
  indicadores deste simulador (o cabeçalho é diferente)`.
- **Arquivo aberto no Excel:** o Excel bloqueia o arquivo e a gravação falha. Feche-o e tente de
  novo.
- Enquanto a janela de salvar está aberta, a simulação não avança.
- Para medir desempenho, exporte com a simulação **pausada ou encerrada**. Uma exportação feita
  com a simulação rodando acrescenta até 0,33 s ao "Tempo decorrido" das leituras seguintes.

O arquivo usa ponto e vírgula (`;`) como separador e vírgula como separador decimal, o padrão das
planilhas em português. As colunas são:

| Coluna | Conteúdo |
|---|---|
| Data e hora | Momento do clique, no formato `dd/MM/aaaa HH:mm:ss`. |
| Arquivo de cenário | Nome do arquivo de cenário carregado. |
| Arquivo de comportamento | Nome do arquivo de comportamento carregado. |
| Estado | `rodando`, `pausada` ou `encerrada` (antes de iniciar, aparece `pausada`). |
| Motivo do encerramento | Preenchido só quando o estado é `encerrada`. |
| Partículas | Número de partículas. |
| Ciclos | Ciclos concluídos. |
| Ciclo do último movimento | Vazio se nenhuma partícula se moveu. |
| Movimentos | Total de movimentos. |
| Colisões | Total de colisões. |
| Movimentos inválidos | Total de movimentos inválidos. |
| Mensagens enviadas | Total de tentativas de envio. |
| Mensagens recebidas | Total de mensagens entregues. |
| Tempo decorrido (s) | Com 3 casas decimais. |
| Tempo médio por ciclo (ms) | O indicador "Tempo por ciclo", com 3 casas decimais. |
| FPS | Com 1 casa decimal. |
| FPS médio | Com 1 casa decimal; vazio antes de iniciar. |

Ao abrir no Excel, as colunas vêm com a largura padrão: a data pode aparecer como `####` e textos
longos podem parecer cortados. Os valores estão completos; basta alargar as colunas.

---

## 5. Exemplos completos

Os arquivos destes exemplos estão em [`docs/exemplos`](exemplos) e na pasta `exemplos` do pacote
do simulador. Para rodar cada um, siga o [passo a passo da seção 4.1](#41-passo-a-passo-com-o-exemplo-1)
trocando os nomes dos arquivos.

### 5.1 Exemplo 1: até a borda

Uma partícula anda para a direita (`X+`) enquanto pode e muda de cor quando chega à borda.

`cenario_borda.json`:

```json
{
  "gridSize": [8, 2, 1],
  "maxCycles": 50,
  "idleCyclesLimit": 3,
  "particles": [
    { "id": 1, "type": "Andarilho", "position": [0, 0, 0] },
    { "id": 2, "type": "Marco", "position": [7, 1, 0] }
  ]
}
```

- A grade tem 8 colunas em X (de 0 a 7), 2 linhas em Y e uma camada em Z.
- O Andarilho começa na coluna 0. O Marco fica na coluna 7, uma linha acima (`y = 1`), fora do
  caminho do Andarilho. Ele só serve para a câmera enquadrar o caminho inteiro desde o início.
- A simulação encerra depois de 3 ciclos sem atividade ou, no máximo, em 50 ciclos.

`comportamento_borda.json`:

```json
{
  "Andarilho": {
    "onInit": [
      { "actions": ["setColor('#3399FF')", "setState('parado', false)"] }
    ],
    "onStep": [
      {
        "condition": "canMove('X+')",
        "actions": ["move('X+')"]
      },
      {
        "condition": "Not canMove('X+') And checkState('parado', '==', false)",
        "actions": ["setColor('#FF6600')", "setState('parado', true)"]
      }
    ]
  },
  "Marco": {
    "onInit": [
      { "actions": ["setColor('#777777')"] }
    ]
  }
}
```

- No `onInit`, o Andarilho fica azul e cria a variável `parado` com o valor `false`.
- Primeira regra do `onStep`: se a célula à direita está livre e dentro da grade, anda.
- Segunda regra: se não pode andar e ainda não tinha parado, fica laranja e marca `parado` como
  `true`. A variável impede que a regra se repita: se ela trocasse a cor em todo ciclo, isso
  contaria como atividade e a simulação nunca encerraria por inatividade.
- O Marco só fica cinza no `onInit`.

Resultado:

| Ciclos | O que acontece |
|---|---|
| 1 a 7 | O Andarilho anda uma coluna por ciclo, da 0 até a 7. |
| 8 | Na coluna 7, `canMove('X+')` é falso (sairia da grade). O Andarilho fica laranja. |
| 9 a 11 | Nenhuma atividade. |
| fim | `Simulação encerrada: sem atividade por 3 ciclo(s).` |

Indicadores finais: Ciclos 11, Movimentos 7, Ciclo do último movimento 7, Colisões 0, Movimentos
inválidos 0, Mensagens enviadas 0, Mensagens recebidas 0.

### 5.2 Exemplo 2: cadeia que repassa uma mensagem

Seis partículas em fila. A primeira avisa a segunda, que acende e repassa o aviso para a
terceira, e assim por diante, uma partícula por ciclo.

`cenario_cadeia.json`:

```json
{
  "gridSize": [6, 1, 1],
  "maxCycles": 50,
  "idleCyclesLimit": 3,
  "particles": [
    { "id": 1, "type": "Inicio", "position": [0, 0, 0] },
    { "id": 2, "type": "Elo", "position": [1, 0, 0] },
    { "id": 3, "type": "Elo", "position": [2, 0, 0] },
    { "id": 4, "type": "Elo", "position": [3, 0, 0] },
    { "id": 5, "type": "Elo", "position": [4, 0, 0] },
    { "id": 6, "type": "Elo", "position": [5, 0, 0] }
  ]
}
```

`comportamento_cadeia.json`:

```json
{
  "Inicio": {
    "onInit": [
      { "actions": ["setColor('#FFCC00')", "sendMessage('X+', 'aviso')"] }
    ]
  },
  "Elo": {
    "onInit": [
      { "actions": ["setColor('#777777')"] }
    ],
    "onMessage(X-)": [
      {
        "condition": "checkMessage('aviso')",
        "actions": ["setColor('#FFCC00')", "sendMessage('X+', 'aviso')"]
      }
    ]
  }
}
```

- O Inicio fica amarelo e, já no `onInit`, envia `'aviso'` para a vizinha da direita (`X+`).
- Cada Elo começa cinza. Quando recebe `'aviso'` vindo da esquerda (`onMessage(X-)`), fica amarelo
  e repassa o aviso para a direita.
- Como cada mensagem chega no ciclo seguinte ao envio, o aviso avança uma partícula por ciclo.

Resultado:

| Ciclo | O que acontece |
|---|---|
| antes do ciclo 1 | O Inicio envia o aviso para a partícula 2. |
| 1 | A partícula 2 recebe, fica amarela e envia para a 3. |
| 2 | A partícula 3 recebe, fica amarela e envia para a 4. |
| 3 | A partícula 4 recebe, fica amarela e envia para a 5. |
| 4 | A partícula 5 recebe, fica amarela e envia para a 6. |
| 5 | A partícula 6 recebe e fica amarela. Ela tenta repassar, mas não há ninguém à direita. |
| 6 a 8 | Nenhuma atividade. |
| fim | `Simulação encerrada: sem atividade por 3 ciclo(s).` |

Indicadores finais: Ciclos 8, Movimentos 0, Mensagens enviadas **6**, Mensagens recebidas **5**.
A diferença é a tentativa da partícula 6: ela conta como envio, mas ninguém recebeu.

### 5.3 Exemplo 3: contagem de saltos

A mesma fila do exemplo 2, mas agora a mensagem leva um número: a posição de cada partícula na
fila. Cada uma guarda a sua posição e repassa a posição seguinte. A última fica verde.

`cenario_contagem.json` é igual ao `cenario_cadeia.json`.

`comportamento_contagem.json`:

```json
{
  "definitions": {
    "fimDaFila": "Not hasNeighbor('X+')"
  },
  "Inicio": {
    "onInit": [
      {
        "actions": [
          "setColor('#FFCC00')",
          "setState('posicao', 0)",
          "sendMessage('X+', 'conta', 1)"
        ]
      }
    ]
  },
  "Elo": {
    "onInit": [
      { "actions": ["setColor('#777777')"] }
    ],
    "onMessage(X-)": [
      {
        "condition": "checkMessage('conta')",
        "actions": [
          "setState('posicao', messageValue())",
          "setState('proximo', messageValue())",
          "setState('proximo', +1)"
        ]
      },
      {
        "condition": "checkMessage('conta') And Not fimDaFila",
        "actions": ["setColor('#FFCC00')", "sendMessage('X+', 'conta', state('proximo'))"]
      },
      {
        "condition": "checkMessage('conta') And fimDaFila",
        "actions": ["setColor('#33CC66')"]
      }
    ]
  }
}
```

- `definitions` cria a condição `fimDaFila`: não há vizinha à direita.
- O Inicio grava `posicao` igual a 0 e envia a etiqueta `'conta'` com carga 1.
- Primeira regra do Elo: lê a carga com `messageValue()` (o `checkMessage` da condição marcou a
  mensagem), grava em `posicao` e calcula `proximo` como a carga mais 1, usando a operação
  relativa `+1`.
- Segunda regra: se não é o fim da fila, fica amarela e envia `'conta'` com a carga
  `state('proximo')`. Como `setState` vale na hora, `proximo` já tem o valor calculado pela regra
  anterior.
- Terceira regra: se é o fim da fila, fica verde e não envia nada.

Resultado: cada partícula termina com `posicao` igual à sua distância até o Inicio (partícula 2:
1, partícula 3: 2, até a partícula 6: 5). As partículas 2 a 5 ficam amarelas e a 6 fica verde no
ciclo 5. A simulação encerra no ciclo 8 com `sem atividade por 3 ciclo(s)`.

Indicadores finais: Ciclos 8, Mensagens enviadas 5, Mensagens recebidas 5. Desta vez os números
são iguais, porque a última partícula não tenta enviar.
