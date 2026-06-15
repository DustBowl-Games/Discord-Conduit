{{/*
Expand the name of the chart.
*/}}
{{- define "discord-conduit.name" -}}
{{- default .Chart.Name .Values.nameOverride | trunc 63 | trimSuffix "-" }}
{{- end }}

{{/*
Create a default fully qualified app name.
Truncated at 63 chars because some Kubernetes name fields are limited to this.
*/}}
{{- define "discord-conduit.fullname" -}}
{{- if .Values.fullnameOverride }}
{{- .Values.fullnameOverride | trunc 63 | trimSuffix "-" }}
{{- else }}
{{- $name := default .Chart.Name .Values.nameOverride }}
{{- if contains $name .Release.Name }}
{{- .Release.Name | trunc 63 | trimSuffix "-" }}
{{- else }}
{{- printf "%s-%s" .Release.Name $name | trunc 63 | trimSuffix "-" }}
{{- end }}
{{- end }}
{{- end }}

{{/*
Create chart name and version as used by the chart label.
*/}}
{{- define "discord-conduit.chart" -}}
{{- printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" | trunc 63 | trimSuffix "-" }}
{{- end }}

{{/*
Common labels.
*/}}
{{- define "discord-conduit.labels" -}}
helm.sh/chart: {{ include "discord-conduit.chart" . }}
{{ include "discord-conduit.selectorLabels" . }}
{{- if .Chart.AppVersion }}
app.kubernetes.io/version: {{ .Chart.AppVersion | quote }}
{{- end }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
{{- end }}

{{/*
Selector labels.
*/}}
{{- define "discord-conduit.selectorLabels" -}}
app.kubernetes.io/name: {{ include "discord-conduit.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
{{- end }}

{{/*
Image tag — defaults to the chart's appVersion when image.tag is empty.
*/}}
{{- define "discord-conduit.imageTag" -}}
{{- default .Chart.AppVersion .Values.image.tag }}
{{- end }}

{{/*
Full image reference (repository:tag).
*/}}
{{- define "discord-conduit.image" -}}
{{- printf "%s:%s" .Values.image.repository (include "discord-conduit.imageTag" .) }}
{{- end }}

{{/*
Name of the Secret holding the bot token (existing one if provided, else the
chart-managed Secret named after the release).
*/}}
{{- define "discord-conduit.tokenSecretName" -}}
{{- if .Values.token.existingSecret }}
{{- .Values.token.existingSecret }}
{{- else }}
{{- include "discord-conduit.fullname" . }}
{{- end }}
{{- end }}

{{/*
Key within the token Secret that holds the token.
*/}}
{{- define "discord-conduit.tokenSecretKey" -}}
{{- if .Values.token.existingSecret }}
{{- default "token" .Values.token.existingSecretKey }}
{{- else }}
{{- "token" }}
{{- end }}
{{- end }}
