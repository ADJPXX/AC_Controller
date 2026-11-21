# AC Controller

O AC Controller é um aplicativo para Windows que controla aparelhos de ar-condicionado Gree e modelos compatíveis com o protocolo local Gree/EWPE Smart. Ele procura aparelhos na rede IPv4 local e se comunica diretamente com o selecionado por UDP. Não exige conta Gree nem serviço em nuvem.

O aplicativo abre o painel de controle ao iniciar e mantém um ícone na área de notificação. Fechar o painel o oculta sem encerrar o aplicativo. Clique no ícone para reabri-lo; use **Sair** no menu do ícone para encerrar.

## Recursos

- Descoberta por broadcast IPv4 e tentativa direta ao último endereço IP conhecido.
- Seleção de um aparelho, com preferência salva pelo endereço MAC.
- Pareamento local e reutilização de sessão salva quando ela ainda é válida.
- Painel redimensionável em português, com controles organizados lado a lado em janelas largas e empilhados com rolagem em janelas estreitas.
- Modos Automático, Frio, Seco, Ventilar e Quente.
- Temperatura configurável de 16 °C a 30 °C.
- Velocidades de ventilação baixa, média-baixa, média, média-alta, alta e Turbo. A velocidade Automática pode aparecer no estado do aparelho, mas não é enviada como ajuste manual.
- Direção das aletas nas posições 1 a 5 ou oscilação contínua. “Padrão do aparelho” é exibido como estado, não como comando manual.
- Atualização periódica do estado, reconexão automática e controles rápidos no menu da área de notificação.

O aplicativo controla um aparelho selecionado por vez. Os ajustes dependem dos recursos e do firmware do aparelho.

## Requisitos de rede

- Windows e .NET 10 SDK para compilar e executar pelo código-fonte.
- O computador e o aparelho devem estar na mesma rede IPv4 alcançável.
- Tráfego UDP local de entrada e saída na porta `7000` precisa ser permitido.
- Internet não é necessária para localizar ou controlar o aparelho.

A descoberta envia broadcasts pelas interfaces de rede elegíveis. Roteadores, VLANs, redes de convidados e isolamento de clientes Wi-Fi podem bloquear broadcasts ou a comunicação entre dispositivos. Broadcasts IPv4 normalmente não atravessam sub-redes; o aplicativo não faz descoberta IPv6.

## Primeiro uso

1. Conecte o computador e o aparelho à mesma rede local.
2. Inicie o AC Controller. O painel abre enquanto a busca local começa.
3. Se um aparelho for encontrado, ele será selecionado automaticamente quando não houver ambiguidade. Se houver vários aparelhos e nenhuma escolha salva, selecione um na janela de seleção.
4. O aplicativo tenta reutilizar a sessão protegida salva. Se ela não funcionar, faz novo pareamento.
5. Acompanhe o indicador de conexão e o horário da última leitura no painel. Use **Selecionar outro aparelho** no menu da área de notificação para trocar a seleção.

A preferência é associada ao MAC do aparelho. Se o endereço IP mudar, uma nova descoberta pode atualizar o endereço salvo.

## Painel e menu da área de notificação

No painel, os comandos de energia, modo, temperatura, ventilação e aletas são enviados ao aparelho conectado. Alterações de clima ficam disponíveis quando há conexão e o aparelho está ligado. O menu do ícone oferece atalhos de energia, modos Frio e Quente, temperaturas, velocidades, direção das aletas, seleção de aparelho, reabertura do painel e saída.

O aplicativo lê o estado aproximadamente a cada três segundos enquanto está conectado. As operações de cada sessão são enfileiradas para evitar que comandos e leituras concorrentes se sobreponham. Após uma falha, ele tenta reconectar com espera crescente: começa em quatro segundos e chega ao limite de quarenta segundos. Uma resposta de rede tem timeout de cinco segundos.

## Como funciona

1. **Descoberta** (`GreeDiscoveryService`): envia consultas UDP para os endereços de broadcast IPv4 das interfaces elegíveis, coleta respostas e remove aparelhos duplicados por MAC. Se o endereço IP conhecido não foi localizado pelo broadcast, tenta consultá-lo diretamente.
2. **Seleção e sessão** (`App`, `TraySelection` e `TrayPreferences`): escolhe o MAC salvo quando disponível, solicita uma escolha quando necessário e tenta carregar a sessão local protegida.
3. **Pareamento** (`GreeBindingService`): atualiza os dados do aparelho e tenta o pareamento com as variantes criptográficas reconhecidas pelo protocolo.
4. **Comandos e estado** (`GreeService` e `GreeNetworkService`): monta e valida mensagens JSON, envia e recebe datagramas UDP, aplica a criptografia exigida pelo aparelho e converte as respostas para o estado usado pela interface.
5. **Interface e ciclo de conexão** (`ControlWindow` e `App`): apresenta estado e comandos no painel WPF e no menu da bandeja, atualiza leituras e coordena cancelamento e reconexão.

O código oferece compatibilidade com mensagens AES-ECB e AES-GCM conforme as variantes encontradas nos aparelhos suportados. Isso descreve compatibilidade de protocolo; não representa um canal de rede seguro de uso geral.

## Arquivos locais e privacidade

Os dados por usuário são gravados em `%LOCALAPPDATA%\AC_Controller`:

| Arquivo | Conteúdo |
| --- | --- |
| `settings.json` | Preferências sem segredo, incluindo MAC selecionado, último IP conhecido e opção `StartWithWindows`. |
| `session.dat` | Dados da sessão local, protegidos com DPAPI para o usuário atual do Windows. |
| `ac-controller.log` | Registro local de eventos e falhas. O log ativo é rotacionado ao atingir 1 MiB, mantendo um arquivo de backup. |

O serviço de inicialização pode registrar o aplicativo na chave `Run` do usuário do Windows quando `StartWithWindows` está habilitado. A versão atual não oferece um controle visível para alterar essa opção; o padrão é desativado, salvo configuração existente ou migrada.

Se ainda não existir o arquivo de preferências por usuário, uma configuração legada `AC_Controller.json` ao lado do executável pode fornecer os dados iniciais. As configurações por usuário têm precedência.

### Segurança

- Use o aparelho somente em uma rede local confiável. Não exponha nem redirecione a porta UDP `7000` para a Internet.
- O protocolo local utiliza chaves compartilhadas de bootstrap para descoberta e pareamento.
- Mensagens AES-ECB não autenticam o conteúdo. AES-GCM é usado com os parâmetros exigidos pelo protocolo do aparelho.
- DPAPI protege o arquivo de sessão para o usuário atual do Windows; não criptografa o tráfego na rede local.

## Compilar, executar e testar

Os projetos usam WPF e `net10.0-windows`; execute os comandos em um ambiente Windows com o SDK .NET 10 instalado. Na raiz do repositório:

~~~powershell
dotnet restore .\AC_Controller.sln
dotnet build .\AC_Controller.sln -c Release
dotnet test .\AC_Controller.sln -c Release --no-build
dotnet run --project .\AC_Controller\AC_Controller.csproj
~~~

A suíte automatizada usa xUnit e NSubstitute e inclui testes de interface WPF, ciclo de vida da bandeja, persistência, rede, criptografia, concorrência e logging. Os testes de rede usam cenários locais/simulados; não precisam controlar um aparelho real.

## Publicar para Windows x64

Para publicar um executável autocontido em arquivo único:

~~~powershell
dotnet publish .\AC_Controller\AC_Controller.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -o .\publish
~~~

O workflow manual do GitHub Actions (`workflow_dispatch`) restaura os pacotes, executa a suíte, publica para Windows x64 e cria uma GitHub Release com `AC_Controller.exe`.

## Limites e solução de problemas

- O aplicativo controla apenas um aparelho por vez e não oferece agendamentos nem histórico persistente de comandos.
- O menu da bandeja oferece atalhos para Frio e Quente; o painel também disponibiliza Automático, Seco e Ventilar.
- A compatibilidade depende do módulo Wi-Fi e do firmware. Nem todo modelo Gree implementa o protocolo e os campos de estado esperados.
- Se nenhum aparelho aparecer, confirme a conexão Wi-Fi, a mesma rede local, as regras de firewall para UDP `7000` e a ausência de isolamento entre clientes.
- Se o aparelho estiver em outra sub-rede, o broadcast provavelmente não o alcançará. Configure conectividade direta entre as redes ou coloque computador e aparelho na mesma sub-rede.
- Para falha de conexão ou pareamento, use **Tentar novamente**, confira a rede e consulte `%LOCALAPPDATA%\AC_Controller\ac-controller.log`.

`AUDITORIA.md` é um relatório histórico da auditoria da versão anterior à inclusão do painel principal; não descreve integralmente a interface atual.

## Estrutura do repositório

~~~text
AC_Controller.sln
AC_Controller/
  App.xaml(.cs)                  Inicialização, bandeja, painel e reconexão
  ControlWindow.xaml(.cs)        Painel de controle responsivo
  DeviceSelectionWindow.xaml/.cs Seletor de aparelho
  Models/                        Configuração, aparelho e estado
  Services/                      Rede, descoberta, protocolo, persistência e logs
  Resources/                     Ícones e recursos visuais
AC_Controller.Tests/             Testes automatizados xUnit
.github/workflows/release.yml    Build, testes e publicação manual
~~~
