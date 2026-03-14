#!/usr/bin/env bash

# =============================================================================
# scripts/deploy.sh
# Full deployment script for DbPoolInsight (Build + Push + Deploy) on Kubernetes
#
# Usage:
#   ./scripts/deploy.sh dev dbpoolinsight --build khaledibrahimahmed latest   # deploy dev overlay
#   ./scripts/deploy.sh prod dbpoolinsight --build khaledibrahimahmed 1.0.1 # # deploy prod overlay
#   ./scripts/deploy.sh loadtest             # run k6 load test job
# =============================================================================
# ── Bash Strict Mode ──────────────────────────────────────────────────────────
# set -e: Exit immediately if any command returns a non-zero (error) status.
# set -u: Exit if an undefined variable is referenced. Prevents silent bugs.
# set -o pipefail: Ensures that if any command in a pipeline fails (e.g., cmd1 | cmd2), 
#                  the entire pipeline fails, rather than just returning the status of cmd2.
set -euo pipefail

# ── Global Variables ──────────────────────────────────────────────────────────
# Use parameter expansion syntax ${VAR:-default} to set default values if none are provided.

ENVIRONMENT="${1:-dev}"          # Defaults to 'dev' if the first argument ($1) is missing
NAMESPACE="${2:-dbpoolinsight}"  # Defaults to 'dbpoolinsight' if the second argument ($2) is missing
SHOULD_BUILD=false
REGISTRY=""
TAG="latest"

# Shift the first two positional arguments so we can parse flags
shift 2 || true 

# Parse optional flags
while [[ $# -gt 0 ]]; do
  case $1 in
    -b|--build)
      SHOULD_BUILD=true
      REGISTRY="${2:? "Error: --build requires a registry (e.g., docker.io/user)"}"
      shift 2
      # Check if a specific tag was provided after the registry
      if [[ $# -gt 0 && ! $1 =~ ^- ]]; then
        TAG="$1"
        shift
      fi
      ;;
    *)
      shift # Ignore unknown flags
      ;;
  esac
done



# Resolve absolute paths safely, ensuring the script can be run from any directory.
# BASH_SOURCE[0] gets the script's path. dirname gets the directory. cd/pwd resolves it to an absolute path.
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# ROOT_DIR is two levels up from Deployments/k8s/scripts
ROOT_DIR="$(dirname "$(dirname "$(dirname "$SCRIPT_DIR")")")"











# ── Helper Functions & UI ─────────────────────────────────────────────────────
# ANSI color codes for terminal output formatting
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color (resets terminal color)

# Note on Bash array parameters:
# $#  -> Length/Count of arguments passed.
# $* -> Treats all arguments as a single concatenated string: "arg1 arg2 arg3"
# $@  -> Treats arguments as an array/collection: ["arg1", "arg2", "arg3"]. Generally preferred.

# Standardized logging functions
# 'echo -e' enables interpretation of backslash escapes (like colors).
# '>&2' redirects the 'die' output to standard error (stderr) instead of stdout.
log()  { echo -e "${BLUE}[INFO]${NC} $*"; }
ok()   { echo -e "${GREEN}[ OK ]${NC} $*"; }
warn() { echo -e "${YELLOW}[WARN]${NC} $*"; }
die()  { echo -e "${RED}[FAIL]${NC} $*" >&2; exit 1; }


# ── Core Functions ────────────────────────────────────────────────────────────

# check_prerequisites
# Validates that the host machine has the necessary tools installed and configured
# before attempting to run deployment tasks.
check_prerequisites(){

  log "Checking prerequisites...."
  
  # 1. Check for kubectl and helm
  # 'command -v' is safer and more POSIX-compliant than 'which'.
  # '$>/dev/null' silences the output. If the command fails, 'die' is executed.
  for cmd in kubectl helm; do
    command -v "$cmd" &>/dev/null || die "$cmd is not installed"
  done 
  
  # 2. Check if kubectl has kustomize capabilities
  # Since kubectl v1.14, kustomize is built-in. We run a help check to verify.
  kubectl kustomize --help &>/dev/null || die "Your kubectl version does not support kustomize"
  
  # 3. Check cluster connectivity
  # Pings the cluster info endpoint. Fails if the user isn't logged into a cluster
  # or if the current kubeconfig context is invalid.
  kubectl cluster-info &>/dev/null || die "kubectl cannot reach the cluster"
  
  ok " Prerequisites Ok "
}



# ── Install / upgrade monitoring stack via Helm ───────────────────────────────

install_monitoring() {
  log "Installing kube-prometheus-stack via Helm..."

  helm repo add prometheus-community https://prometheus-community.github.io/helm-charts 2>/dev/null || true
  helm repo update

  helm upgrade --install monitoring prometheus-community/kube-prometheus-stack \
    --namespace "$NAMESPACE" \
    --create-namespace \
    -f "$ROOT_DIR/Deployments/k8s/helm/monitoring-values.yaml"

  ok "Monitoring stack installed"

  log "Applying EFCore alert rules..."
  kubectl apply -f "$ROOT_DIR/Deployments/k8s/helm/efcore-rules.yaml" -n "$NAMESPACE"
  ok "Alert rules applied"

  # Apply ServiceMonitor so Prometheus scrapes efcore-api metrics
  log "Applying EFCore ServiceMonitor..."
  kubectl apply -f "$ROOT_DIR/Deployments/k8s/helm/efcore-servicemonitor.yaml" -n "$NAMESPACE"
  ok "ServiceMonitor applied"
}
# ── Load Grafana dashboard as ConfigMap ───────────────────────────────────────
load_dashboard() {
  local DASHBOARD_PATH="$ROOT_DIR/Deployments/k8s/helm/monitoring/grafana/dashboards/efcore-dashboard.json"



  if [ -f "$DASHBOARD_PATH" ]; then
    log "Loading Grafana dashboard ConfigMap..."
    kubectl create configmap efcore-dashboard-configmap \
      --from-file=efcore-dashboard.json="$DASHBOARD_PATH" \
      -n "$NAMESPACE" \
      --dry-run=client -o yaml | kubectl apply -f -

    kubectl label configmap efcore-dashboard-configmap grafana_dashboard=1 -n dbpoolinsight --overwrite
    ok "Dashboard ConfigMap applied"
  else
    warn "Dashboard file not found at $DASHBOARD_PATH — skipping"
    warn "Create the ConfigMap manually or place the dashboard JSON at the expected path"
  fi
}








# deploy_app
# Handles the actual deployment to Kubernetes using Kustomize overlays.
deploy_app(){
  
# Ensure no trailing newlines or spaces in the path
    local OVERLAY_PATH="$ROOT_DIR/Deployments/k8s/overlays/$ENVIRONMENT"
    
    if [ ! -d "$OVERLAY_PATH" ]; then
        die "Overlay path not found: $OVERLAY_PATH"
    fi

    if [ "$SHOULD_BUILD" = true ]; then
        log "Updating Kustomize image tag to $TAG..."
        
        # Check if kustomize is installed for the 'edit' command
        if command -v kustomize &>/dev/null; then
            (cd "$OVERLAY_PATH" && kustomize edit set image "$REGISTRY/efcore-api:$TAG")
        else
            warn "Standalone 'kustomize' not found. Falling back to 'sed' for manifest update."
            # Fallback: Find the image line and replace the tag
            sed -i "s|newTag:.*|newTag: \"$TAG\"|" "$OVERLAY_PATH/kustomization.yaml"
            # We call it directly since it's in the same folder
            "$SCRIPT_DIR/build-push.sh" "$REGISTRY" "$TAG"
        fi
    fi


  log "Deploying $ENVIRONMENT overlay..."
  
  # 'kubectl apply -k' processes the kustomization.yaml in the target directory
  # and applies the resulting manifests to the cluster.
  kubectl apply -k "$OVERLAY_PATH"
  ok "Kustomize overlay applied"

  log "Waiting for SQL Server to be ready..."
  # Halts script execution until the sqlserverdb deployment is fully rolled out.
  # Times out and fails the script if it takes longer than 3 minutes.--timeout=5m
  kubectl rollout status deployment/sqlserverdb -n "$NAMESPACE" 

  log "Waiting for EFCore API to be ready..."
  # Halts script execution until the efcore-api deployment is ready (2-minute timeout).
  kubectl rollout status deployment/efcore-api -n "$NAMESPACE" 

  ok "Deployment complete!"
}

# ── Print access info ─────────────────────────────────────────────────────────
print_access_info() {
  echo ""
  echo -e "${GREEN}═══════════════════════════════════════════════════════${NC}"
  echo -e "${GREEN}  DbPoolInsight deployed to: $ENVIRONMENT${NC}"
  echo -e "${GREEN}═══════════════════════════════════════════════════════${NC}"
  echo ""

# currently we implement for both dev and prod 
    echo "  Port-forward commands (run in separate terminals):"
    echo "    kubectl port-forward svc/efcore-api 8080:8080 -n $NAMESPACE"
    echo "    kubectl port-forward svc/monitoring-grafana 3000:80 -n $NAMESPACE"
    echo "    kubectl port-forward svc/monitoring-kube-prometheus-prometheus 9090:9090 -n $NAMESPACE"
    echo ""
    echo ""
    echo "  Then open:"
    echo "    API:        http://localhost:8080/swagger/index.html"
    echo "    OTEL:       http://localhost:8080/metrics"
    echo "    HEALTH:     http://localhost:8080/health"
    echo "    Grafana:    http://localhost:3000  (admin / admin)"
    echo "    Prometheus: http://localhost:9090"
  echo ""
}

# ── Run k6 load test ──────────────────────────────────────────────────────────
run_loadtest() {
  log "Applying k6 ConfigMap and Job..."
 
  # Delete any previous job run
  kubectl delete job k6-loadtest -n "$NAMESPACE" --ignore-not-found
 
  kubectl apply -f "$ROOT_DIR/Deployments/k8s/base/k6/configmap.yaml" -n "$NAMESPACE"
  kubectl apply -f "$ROOT_DIR/Deployments/k8s/base/k6/job.yaml"       -n "$NAMESPACE"
 
  log "k6 job started. Stream logs with:"
  echo kubectl logs -f job/k6-loadtest -n $NAMESPACE
}


# ── Main Execution Controller ─────────────────────────────────────────────────

main(){
  # Optional: You can enforce that at least one argument is provided by uncommenting the block below.
  # Since you mapped defaults for $1 and $2 above, this check might conflict if you want
  # to allow parameterless execution (defaulting to dev). 
  #
  # if [ "$#" -lt 1 ]; then
  #   die "Usage: $0 <environment> (dev|prod|loadtest)"
  # fi
  
  check_prerequisites

  # Route the script's behavior based on the specified environment
  case "$ENVIRONMENT" in 
    dev|prod)
      log "Running $ENVIRONMENT ... "
      deploy_app
      load_dashboard      # ← Create the ConfigMap BEFORE Helm installs Grafana
      install_monitoring
      print_access_info
      ;;
    loadtest)
      # Execution block for LOADTEST environment
      log "Running $ENVIRONMENT ... "
      run_loadtest
      ;; # Break out of case statement
    
    
    *) 
      # Default catch-all block for unhandled or invalid environment arguments
      die "Unknown environment: $ENVIRONMENT. Use: dev | prod | loadtest"
      ;;
  esac 
}

# ── Entry Point ───────────────────────────────────────────────────────────────
# Passes all script arguments directly into the main function.
# Using "$@" ensures that arguments with spaces are preserved correctly.
main "$@"



#Troubleshooting 
# kubectl delete pod monitoring-grafana-dbc8bffbf-g2x9w -n dbpoolinsight --force
# kubectl delete pvc monitoring-grafana -n dbpoolinsight
# helm upgrade monitoring prometheus-community/kube-prometheus-stack `  --namespace dbpoolinsight `  -f C:\Users\ZALL-TECH\Desktop\Projects\DbPoolInsight\DbPoolInsight\Deployments\k8s\helm\monitoring\grafana\dashboards\efcore-dashboard.json
# helm upgrade monitoring prometheus-community/kube-prometheus-stack --namespace dbpoolinsight -f "C:\Users\ZALL-TECH\Desktop\Projects\DbPoolInsight\DbPoolInsight\Deployments\k8s\helm\monitoring-values.yaml"
# kubectl rollout restart deployment/monitoring-grafana -n dbpoolinsight


# kubectl create configmap efcore-dashboard-configmap --from-file=efcore-dashboard.json=C:\Users\ZALL-TECH\Desktop\Projects\DbPoolInsight\DbPoolInsight\Deployments\k8s\helm\monitoring\grafana\dashboards\efcore-dashboard.json -n dbpoolinsight --dry-run=client -o yaml | kubectl apply -f -
# kubectl label configmap efcore-dashboard-configmap grafana_dashboard=1 -n dbpoolinsight --overwrite




# docker pull gcr.io/k8s-minikube/kicbase:v0.0.50
