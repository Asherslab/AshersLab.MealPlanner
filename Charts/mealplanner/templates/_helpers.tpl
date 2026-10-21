{{- define "mealplanner.name" -}}
{{- .Release.Name | trunc 63 | trimSuffix "-" -}}
{{- end -}}

{{- define "mealplanner.labels" -}}
app.kubernetes.io/name: mealplanner
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/version: {{ .Values.image.tag | default .Chart.AppVersion | quote }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
helm.sh/chart: {{ printf "%s-%s" .Chart.Name .Chart.Version }}
{{- end -}}

{{- define "mealplanner.selector" -}}
app.kubernetes.io/name: mealplanner
app.kubernetes.io/instance: {{ .Release.Name }}
{{- end -}}

{{- define "mealplanner.restricted" -}}
allowPrivilegeEscalation: false
readOnlyRootFilesystem: true
capabilities:
  drop: [ALL]
{{- end -}}

{{- define "mealplanner.claim" -}}
{{- .Values.persistence.existingClaim | default (printf "%s-data" (include "mealplanner.name" .)) -}}
{{- end -}}

{{/* Refuse at install what the app would refuse at start, with the same reasons - a crash-looping pod says less. */}}
{{- define "mealplanner.check" -}}
{{- if not (has .Values.auth.mode (list "TrustedHeader" "None")) -}}
{{- fail "auth.mode must be TrustedHeader or None." -}}
{{- end -}}
{{- if and (eq .Values.auth.mode "TrustedHeader") (not .Values.auth.trustedProxies) -}}
{{- fail "auth.trustedProxies is required for TrustedHeader: the sign-in proxy's addresses (the pod network in-cluster)." -}}
{{- end -}}
{{- if and (eq .Values.auth.mode "TrustedHeader") (not .Values.networkPolicy.enabled) (contains "/" .Values.auth.trustedProxies) -}}
{{- fail "networkPolicy.enabled is off but auth.trustedProxies is a network: every pod in it could sign in as anyone. Turn the policy on, or name the proxy's own addresses." -}}
{{- end -}}
{{- if and .Values.networkPolicy.enabled (not .Values.networkPolicy.fromNamespaceLabels) -}}
{{- fail "networkPolicy.fromNamespaceLabels is required: the ingress controller's namespace." -}}
{{- end -}}
{{- if .Values.litestream.enabled -}}
{{- if not .Values.litestream.bucket -}}
{{- fail "litestream.bucket is required when litestream is enabled." -}}
{{- end -}}
{{- if not .Values.litestream.existingSecret -}}
{{- fail "litestream.existingSecret is required when litestream is enabled: a Secret with LITESTREAM_ACCESS_KEY_ID and LITESTREAM_SECRET_ACCESS_KEY." -}}
{{- end -}}
{{- end -}}
{{- end -}}
