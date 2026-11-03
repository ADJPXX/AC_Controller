# AC Controller

Aplicativo Windows de bandeja para controlar aparelhos de ar-condicionado Gree e compatíveis com o protocolo local Gree/EWPE Smart.

## Primeira execução

1. Conecte o computador e o ar-condicionado à mesma rede local.
2. Inicie o AC Controller.
3. O aplicativo procura aparelhos na rede e negocia automaticamente o endereço IP, o MAC e a chave local do aparelho.
4. Se houver somente um aparelho compatível, ele será selecionado. Se houver vários e nenhum aparelho tiver sido salvo antes, escolha a unidade na janela apresentada.

Depois da primeira conexão, o aplicativo identifica a unidade pelo MAC. O endereço IP é atualizado sempre que ela é encontrada, então uma mudança de IP não exige editar arquivo de configuração. A chave da sessão fica protegida pelo Windows e vinculada à conta atual.

O programa funciona pela rede local e não precisa de conta Gree, serviço em nuvem ou preenchimento manual de `DeviceKey`.

## Uso

O ícone do AC Controller aparece na área de notificação do Windows. O menu mostra estado, modo, temperatura, velocidade do ventilador e direção das aletas. Também permite ligar, desligar e alterar as opções disponíveis.

Se quiser controlar outra unidade, escolha **Selecionar outro aparelho…** no menu. O MAC da nova unidade será salvo e usado nas próximas inicializações.

## Rede e solução de problemas

- O computador precisa conseguir enviar e receber UDP na porta 7000.
- A descoberta automática usa broadcast IPv4; roteadores normalmente não encaminham broadcast entre redes ou VLANs.
- Se nada for encontrado, confirme que o aparelho está conectado ao Wi-Fi, que o computador está na rede correta e que o firewall do Windows permite tráfego local para o aplicativo.
- O menu da bandeja oferece **Tentar novamente** e **Selecionar aparelho…**. O aplicativo também tenta reconectar automaticamente com intervalos crescentes.
- O log local fica em `%LOCALAPPDATA%\AC_Controller\ac-controller.log`.

Configurações não secretas ficam em `%LOCALAPPDATA%\AC_Controller\settings.json`. A sessão, incluindo a chave do aparelho, fica em `session.dat`, protegida com DPAPI do Windows. A chave não é gravada no JSON nem nos logs.

## Segurança

O protocolo local Gree usa chaves genéricas compartilhadas entre implementações e o tráfego não deve ser tratado como protegido contra outros dispositivos da mesma rede. Mantenha o ar-condicionado em uma rede confiável e não encaminhe UDP/7000 pela internet. O IV GCM fixo é exigido pelo protocolo do aparelho; gerar um IV aleatório localmente tornaria os pacotes incompatíveis.

## Compilar

Requer o SDK .NET 10 para Windows. Abra `AC_Controller.sln` no Visual Studio ou compile com `dotnet build AC_Controller.sln`.
