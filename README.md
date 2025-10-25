# Coletor de Arquivos

Aplicativo desktop em WPF para localizar e copiar arquivos de múltiplas pastas de origem com base em extensões definidas pelo usuário. A ferramenta facilita reunir arquivos dispersos em um único diretório de destino, apresentando informações detalhadas sobre o progresso, tamanho agregado e possíveis problemas encontrados durante a busca ou cópia.

## Requisitos

- Windows 10 ou superior.
- [.NET SDK 8.0](https://dotnet.microsoft.com/pt-br/download) (inclui o runtime necessário).
- Visual Studio 2022, Visual Studio Code com extensões para .NET, ou outro ambiente compatível para compilar e executar aplicações WPF.

## Como compilar e executar

1. Clone ou baixe este repositório.
2. Abra um terminal na raiz do projeto e execute:

   ```bash
   dotnet build ColetorDeArquivos.sln
   ```

   Esse comando compila a solução e garante que todas as dependências estejam resolvidas.
3. Para executar a aplicação diretamente pelo SDK, utilize:

   ```bash
   dotnet run --project ColetorDeArquivos/ColetorDeArquivos.csproj
   ```

   Você também pode abrir a solução `ColetorDeArquivos.sln` no Visual Studio e iniciar a depuração (F5) ou a execução sem depurar (Ctrl+F5).

## Uso da aplicação

1. **Defina as pastas de origem**
   - Clique em **Adicionar...** no painel "Pastas de origem" para escolher diretórios onde os arquivos serão procurados.
   - Utilize **Remover selecionadas** para excluir entradas específicas ou **Limpar** para esvaziar a lista.

2. **Escolha a pasta de destino**
   - Clique em **Selecionar...** no grupo "Pasta de destino" para apontar onde os arquivos encontrados serão copiados.

3. **Informe as extensões desejadas**
   - No campo "Extensões", digite as extensões a filtrar separadas por vírgulas ou espaços (ex.: `pdf, docx, xlsx`).
   - Deixe em branco para buscar todos os arquivos.

4. **Configure as opções**
   - **Sobrescrever arquivos existentes**: substitui arquivos com o mesmo nome ao copiar para o destino.
   - **Simulação (não copia)**: realiza apenas a varredura, sem efetuar a cópia dos arquivos listados.
   - **Seguir links simbólicos**: inclui arquivos apontados por symlinks durante a busca.

5. **Execute a busca**
   - Clique em **Iniciar busca**. Um indicador de progresso mostra quando a aplicação está trabalhando.
   - O painel central exibirá a lista de arquivos encontrados com seus caminhos, tamanhos formatados e status.
   - Um resumo acima da lista apresenta a contagem total de itens e o tamanho agregado ocupado por todos os resultados.

6. **Selecione e copie arquivos**
   - Marque itens individualmente ou use **Selecionar tudo**/**Limpar seleção**.
   - Utilize **Copiar selecionados** para transferir apenas os escolhidos ou **Copiar todos** para mover todos os resultados.
   - Acompanhe as mensagens no painel de logs para confirmar operações concluídas ou erros.

7. **Cancelar ações**
   - Enquanto uma busca ou cópia estiver em andamento, os botões **Cancelar busca** e **Cancelar cópia** ficam habilitados para interromper o processo atual.

## Registro de atividades

A área inferior da janela mantém um log contínuo com eventos informativos, avisos e erros. As mensagens ajudam a identificar arquivos que não puderam ser processados, permissões insuficientes ou conflitos ao copiar.

## Licença

Este projeto é disponibilizado sob a licença especificada no arquivo `LICENSE`, caso exista. Caso contrário, considere-o apenas para uso interno até que uma licença seja definida.
