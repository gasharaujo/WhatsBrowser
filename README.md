# WhatsBrowser para Windows

Aplicativo WinForms com o site real `web.whatsapp.com` incorporado no Microsoft Edge WebView2. Use a conta do WhatsApp vinculada pelo QR Code, sem WABA.

## Abrir o aplicativo

Abra `dist\WhatsBrowser.exe`. Mantenha na mesma pasta os arquivos publicados junto com o executável. Esta compilação é para Windows x64 com .NET Desktop Runtime 8 e Microsoft Edge WebView2 Runtime.

Para compilar novamente com o SDK .NET 8:

```powershell
dotnet restore WhatsBrowser.csproj --packages .packages
dotnet publish WhatsBrowser.csproj -c Release --no-restore -o dist
```

## Usar com uma conversa real

1. Abra o app. Se aparecer o QR Code, vincule a sessão em **Aparelhos conectados** no WhatsApp do celular.
2. Abra a conversa desejada no painel do WhatsApp Web e vá para a mensagem mais recente.
3. Na aba **Fluxos**, escolha um fluxo ou clique em **+ Fluxo**. A entrada inicial abre o canvas. Na entrada selecionada, use **+ Saída** para configurar uma condição, uma resposta e a próxima entrada.
4. Clique em **Salvar** para manter o fluxo entre sessões.
5. Para aplicar a regra à última mensagem recebida, clique em **Preparar resposta**. O texto aparece no campo do WhatsApp para revisão e envio.
6. Para responder a novas mensagens da conversa aberta, clique em **Ativar automático** e confirme a conversa no diálogo. A barra inferior mostra a entrada em que o fluxo está aguardando. Clique em **Pausar automático** quando terminar.

O aplicativo pausa o automático se você trocar a conversa ou alterar as regras. Ao navegar pelo histórico, volte ao fim da conversa para retomar o acompanhamento. Se o campo já contiver um rascunho, ele é preservado. A leitura e o envio usam a conversa aberta no WhatsApp Web.

### Enviar a primeira mensagem

Em **Fluxos → Mensagem inicial…**, configure o texto, marque a opção de envio e aplique. Clique em **Salvar** para manter a configuração. Na conversa aberta, clique em **Ativar automático** e escolha **Enviar e ativar** para mandar a mensagem inicial uma vez. O fluxo então aguarda a primeira resposta na entrada inicial. Escolha **Só ativar** para retomar a conversa sem repetir a mensagem. **Testar** mostra o envio inicial antes das interações digitadas.

### Canvas de várias interações

Crie várias saídas numa entrada e use **↑** e **↓** para definir a ordem. Para cada mensagem, a primeira condição que corresponder é aplicada. Em **Próxima**, escolha uma entrada existente, clique em **+ Próxima entrada** ou ligue a saída clicando no ponto da direita e depois na entrada de destino. O fluxo espera a mensagem seguinte na entrada de destino. **Finalizar e voltar ao início** encerra o caminho atual. Uma mensagem sem condição correspondente mantém a entrada atual.

Arraste os títulos para mover os blocos e o fundo para navegar. Use a roda para alterar o zoom e **Enquadrar** para ver o conjunto. **Testar** aceita uma mensagem por linha e mostra a saída, a resposta e a próxima entrada de cada interação. Você pode executar lotes sucessivos no mesmo teste.

### Regex e normalização

Em **Condição**, selecione **Regex** e informe a expressão completa no formato `/padrão/flags`. Exemplo `/^pedido\s+\d+$/i` corresponde a “Pedido 42”.

**Normalizar entrada** aplica esta transformação antes de avaliar as condições:

```js
entrada.normalize("NFD")
  .replace(/[\u0300-\u036f]/g, "")
  .replace(/[^a-zA-Z0-9 ]/g, "")
  .replace(/\s+/g, " ")
  .trim()
```

### Respostas prontas

Na aba **Respostas prontas**, **Salvar resposta** sempre cria uma entrada. Para editar, selecione o título, altere os campos e clique em **Atualizar**. Com uma conversa aberta, clique em **Enviar** para enviar a mensagem. A barra inferior informa se a mensagem apareceu na conversa ou se a confirmação está pendente. Um rascunho diferente é preservado.

### Print da conversa

Abra a conversa e vá até o trecho desejado. Clique em **Print da conversa**, selecione a primeira mensagem e clique em **Capturar PNG**. A imagem captura os pixels do WhatsApp Web até a última mensagem carregada na lista, incluindo as mídias que estiverem renderizadas. Em trechos longos, o app rola a conversa durante a captura e restaura a posição inicial.

O perfil do WhatsApp Web, os fluxos e as respostas prontas ficam em `%LOCALAPPDATA%\WhatsBrowser`. O app usa o mesmo perfil a cada abertura. A biblioteca incorpora o fluxo salvo na versão anterior como **Fluxo principal**.

## Preparação para Android

O arquivo `RuleEngine.cs` reúne o motor e o formato JSON versionado, independentes do WinForms e da WebView2. O acesso à conversa fica em `WhatsAppBridge.js`. A interface e o transporte entre o app e a WebView ficam na camada Windows. **Importar** e **Exportar** permitem levar o fluxo para uma futura versão.
