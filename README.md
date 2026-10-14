# Programmable Matter Simulator

Simulador de matéria programável: um conjunto de partículas (cubos) ocupa uma grade
tridimensional e, a cada ciclo, cada partícula decide o que fazer seguindo regras escritas por
você. As partículas podem se mover uma célula por vez, guardar valores, mudar de cor e trocar
mensagens com as vizinhas. Todas agem ao mesmo tempo, como numa animação quadro a quadro, e o
simulador resolve sozinho os conflitos quando duas querem ocupar a mesma célula.

Você descreve o experimento em dois arquivos de texto no formato JSON: o **arquivo de cenário**
(tamanho da grade e posição inicial de cada partícula) e o **arquivo de comportamento** (as regras
de cada tipo de partícula). Não é preciso saber Unity nem C#. Durante a execução, o simulador
mostra a simulação em 3D e um painel de indicadores (movimentos, colisões, mensagens, ciclos,
tempo), que podem ser exportados para uma planilha.

## Requisitos

- Windows 64 bits.
- Não precisa de instalação.

## Como baixar e executar

1. Abra a página de [versões publicadas](https://github.com/Loukanos/Programmable-Matter-Simulator/releases)
   deste repositório e baixe o pacote da versão **v1.0 (TCC III)**.
2. Extraia o arquivo `.zip` para uma pasta do seu computador.
3. Dentro da pasta extraída, abra `Programmable Matter Simulator.exe`.

O pacote também traz a pasta `exemplos`, com os mesmos arquivos de
[`docs/exemplos`](docs/exemplos) deste repositório.

## Exemplo mínimo

Uma partícula azul anda para a direita até a borda da grade e fica laranja quando não consegue
mais avançar. Uma segunda partícula, cinza e parada, só marca o fim do caminho.

Arquivo de cenário (`cenario_borda.json`):

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

Arquivo de comportamento (`comportamento_borda.json`):

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

Para rodar:

1. No painel da direita, em **CONFIGURAÇÃO DO CENÁRIO**, clique em **+ Carregar arquivo** e escolha
   `cenario_borda.json`.
2. Em **COMPORTAMENTO DAS PARTÍCULAS**, clique em **+ Carregar arquivo** e escolha
   `comportamento_borda.json`.
3. Clique em **Carregar simulação** e depois no botão **Iniciar** (o triângulo no alto da tela).

A partícula anda 7 células em 7 ciclos, fica laranja no ciclo 8 e a simulação encerra sozinha no
ciclo 11, depois de 3 ciclos sem nenhuma atividade.

## Documentação

O [Manual do Usuário](docs/ManualDoUsuario.md) explica os conceitos, a referência completa dos
dois arquivos, a interface passo a passo e exemplos comentados.

## Licença

Distribuído sob a licença MIT. Veja o arquivo [LICENSE](LICENSE).
