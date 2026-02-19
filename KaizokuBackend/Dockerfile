
FROM mcr.microsoft.com/dotnet/aspnet:8.0-noble

ARG TARGETPLATFORM
ARG TARGETARCH
ARG TARGETOS

# ---- System deps for Chromium/CEF (JCEF) + headless X ----
RUN apt-get update && \
 apt-get -y install --no-install-recommends -y gettext-base unzip tini libicu-dev gosu cron libxss1 libxext6 libxrender1 libxcomposite1 libxdamage1 libxkbcommon0 libxtst6 \
 libgtk-3-0 libjogl2-jni libgluegen2-jni libglib2.0-0t64 libnss3 libdbus-1-3 libpango-1.0-0 libcairo2 libasound2t64 \
 libatk-bridge2.0-0t64 libcups2t64 libdrm2 libgbm1 xvfb && \
 rm -rf /var/lib/apt/lists/*

ENV QT_X11_NO_MITSHM=1
RUN mkdir /tmp/.X11-unix && chmod 1777 /tmp/.X11-unix
      
ENV HOME=/config/mihon
WORKDIR /app
COPY ./bin/$TARGETPLATFORM/ .
COPY ./scripts/entrypoint.sh /app/entrypoint.sh
RUN chmod +x /app/entrypoint.sh
EXPOSE 9833
ENTRYPOINT ["tini", "--"]
CMD ["/app/entrypoint.sh"]