# Engines da demonstração

As engines ficam dentro desta pasta quando a demonstração é preparada. Os binários não fazem parte do histórico Git do código; são instalados por `tools/Install-Engines.ps1` ou incluídos no ZIP completo da demonstração. As instalações pessoais e seu acervo não são utilizados.

| Pasta | Versão | Origem | SHA-256 do pacote |
|---|---|---|---|
| `Mugen` | M.U.G.E.N 1.1 Beta 1 | Espelho `Tunlan/mugen-1.1b1`, commit `5ed02054fe5e80c24259b9da1f7d129b995676c9` | `42A178ECFCCE327355B60D029A82A38B351650E19B9651F810792E2920C123B3` |
| `Ikemen` | IKEMEN GO v1.0.0-rc.3, Windows | Release oficial do projeto | `F7A144DCA8393C4B1B21A904E409CD2FFFB63B098BB73F9A5953DFB0077F5B7E` |

- [Espelho do pacote M.U.G.E.N](https://github.com/Tunlan/mugen-1.1b1/tree/5ed02054fe5e80c24259b9da1f7d129b995676c9). O espelho é uma fonte de terceiros; o hash fixa o pacote usado, sem representar uma assinatura da Elecbyte.
- [Release oficial do IKEMEN GO](https://github.com/ikemen-engine/Ikemen-GO/releases/tag/v1.0.0-rc.3). O hash confere com o SHA256SUMS publicado nessa release.

Os executáveis são distribuídos sem alterações. Os pacotes incluem somente seus recursos de exemplo, sem screenpacks ou coleções pessoais adicionadas pelo DreamMover. Configurações de execução e logs gerados posteriormente são dados locais.

## Termos preservados

- M.U.G.E.N: licença da Elecbyte em `Mugen/readme.txt`, também preservada em `licenses/Mugen.readme.txt`. A redistribuição está sujeita aos termos gratuitos e não comerciais do ambiente. Os recursos padrão possuem termos próprios descritos no mesmo arquivo.
- IKEMEN GO: engine e scripts sob MIT, com recursos e dependências sob outras licenças. O arquivo completo está em `Ikemen/LICENSES.txt`, também preservado em `licenses/Ikemen.LICENSES.txt`.
- A release do IKEMEN oferece o código-fonte do FFmpeg incluído em [src_ffmpeg.tar.gz](https://github.com/ikemen-engine/Ikemen-GO/releases/download/v1.0.0-rc.3/src_ffmpeg.tar.gz). Consulte os termos das bibliotecas incluídas no arquivo de licenças.

Preserve os avisos e arquivos de licença ao redistribuir o pacote de demonstração. A licença MIT do DreamMover aplica-se ao código do aplicativo.
