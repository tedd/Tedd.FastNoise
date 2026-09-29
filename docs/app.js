const reducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;

if (reducedMotion || !("IntersectionObserver" in window)) {
  document.querySelectorAll(".reveal").forEach((element) => element.classList.add("visible"));
} else {
  const observer = new IntersectionObserver((entries) => {
    for (const entry of entries) {
      if (!entry.isIntersecting) continue;
      entry.target.classList.add("visible");
      observer.unobserve(entry.target);
    }
  }, { threshold: 0.08 });

  document.querySelectorAll(".reveal").forEach((element) => observer.observe(element));
}

document.querySelectorAll("[data-copy]").forEach((button) => {
  button.addEventListener("click", async () => {
    await navigator.clipboard.writeText(button.dataset.copy);
    const previous = button.textContent;
    button.textContent = "Copied";
    window.setTimeout(() => { button.textContent = previous; }, 1200);
  });
});

const benchmarkData = window.fastNoiseBenchmarks;
const benchmarkGraphs = document.getElementById("benchmark-graphs");
if (benchmarkData && benchmarkGraphs) {
  const shapeSelect = document.getElementById("benchmark-shape");
  const fractalSelect = document.getElementById("benchmark-fractal");
  const tableBody = document.querySelector("#benchmark-table tbody");
  const algorithms = ["OpenSimplex2", "OpenSimplex2S", "Perlin", "Value", "ValueCubic", "Cellular"];
  const accesses = ["Point loop", "Scalar fill", "SIMD fill", "Parallel fill"];
  const names = { ValueCubic: "Value Cubic" };
  const classes = { "Point loop": "point", "Scalar fill": "scalar", "SIMD fill": "simd", "Parallel fill": "parallel" };
  const formatMs = (value) => `${value < 10 ? value.toFixed(3) : value.toFixed(2)} ms`;

  const machine = document.getElementById("benchmark-machine");
  machine.textContent = `Measured ${new Date(benchmarkData.generatedAt).toLocaleDateString(undefined, { year: "numeric", month: "short", day: "numeric" })} on ${benchmarkData.cpu}; .NET ${benchmarkData.runtime.replace(/^\.NET\s*/, "")}, ${benchmarkData.logicalProcessors} logical processors. `;

  function renderBenchmarks() {
    benchmarkGraphs.replaceChildren();
    tableBody.replaceChildren();
    const selection = benchmarkData.results.filter((row) => row.shape === shapeSelect.value && row.fractal === fractalSelect.value);

    for (const algorithm of algorithms) {
      const rows = accesses.map((access) => selection.find((row) => row.algorithm === algorithm && row.access === access));
      if (rows.some((row) => !row)) continue;
      const scale = Math.max(...rows.map((row) => row.p90Ms)) * 1.05;

      const card = document.createElement("figure");
      card.className = "benchmark-card";
      const title = document.createElement("figcaption");
      title.textContent = names[algorithm] || algorithm;
      card.append(title);

      const tableRow = document.createElement("tr");
      const heading = document.createElement("td");
      heading.textContent = names[algorithm] || algorithm;
      tableRow.append(heading);

      for (const row of rows) {
        const line = document.createElement("div");
        line.className = "benchmark-row";
        const label = document.createElement("span");
        label.className = "benchmark-row-label";
        label.textContent = row.access;
        const track = document.createElement("span");
        track.className = "benchmark-track";
        track.setAttribute("aria-hidden", "true");
        const bar = document.createElement("span");
        bar.className = `benchmark-bar ${classes[row.access]}`;
        bar.style.width = `${(100 * row.medianMs / scale).toFixed(2)}%`;
        const range = document.createElement("span");
        range.className = "benchmark-range";
        range.style.left = `${(100 * row.p10Ms / scale).toFixed(2)}%`;
        range.style.width = `${(100 * (row.p90Ms - row.p10Ms) / scale).toFixed(2)}%`;
        track.append(bar, range);
        const value = document.createElement("span");
        value.className = "benchmark-value";
        value.textContent = formatMs(row.medianMs);
        value.title = `10th–90th percentile: ${formatMs(row.p10Ms)}–${formatMs(row.p90Ms)}`;
        line.append(label, track, value);
        card.append(line);

        const cell = document.createElement("td");
        cell.textContent = formatMs(row.medianMs);
        cell.title = value.title;
        tableRow.append(cell);
      }

      benchmarkGraphs.append(card);
      tableBody.append(tableRow);
    }
  }

  shapeSelect.addEventListener("change", renderBenchmarks);
  fractalSelect.addEventListener("change", renderBenchmarks);
  renderBenchmarks();
}
