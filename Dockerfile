FROM mcr.microsoft.com/dotnet/sdk:8.0-jammy AS build
LABEL org.opencontainers.image.source=https://github.com/Notalib/LibLouis.NET/

RUN apt update && \
    apt upgrade -y && \
    # List of gcc compilers is sadly platform specific, this one works for amd64.
    apt install -y build-essential build-essential gcc-aarch64-linux-gnu gcc-mingw-w64-i686 gcc-mingw-w64-x86-64 gcc-i686-linux-gnu unzip m4 less

WORKDIR /source
ADD . /source

RUN sh ./build.sh

FROM scratch
COPY --from=build /packages/* /
