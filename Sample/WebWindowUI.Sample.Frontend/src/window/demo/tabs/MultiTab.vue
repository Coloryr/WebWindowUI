<script setup lang="ts">
// 多窗口共享 tab：tab 内联展示共享实例；「打开多窗口演示」经 demo.openMulti() 命令
// 开 3 个真实 MultiWindow（共享A/B 绑同一实例 + 独立实例），任一窗口改动全广播、其余跟随。
const props = defineProps<{ model: any; demo: any }>()
</script>

<template>
  <section class="card">
    <h2>一个 model 给多个窗口用，互不干扰</h2>
    <p>
      当前 tab 内联展示共享实例（实例 id 见下）。「打开多窗口演示」用<strong>同一个</strong>实例开
      <strong>3 个真实窗口</strong>：共享A / 共享B 绑同一实例（count 每秒推送、改 name 跨窗口同步）；
      独立实例窗口用另一个实例（count/name 各走各）。看标题栏/此处实例 id 即可区分。
    </p>
    <label class="row">
      <span>Name（共享实例下任一窗口改，其余窗口跟随）</span>
      <input class="input" v-model="props.model.name" placeholder="输入后回写给 .NET 并广播" />
    </label>
    <div class="row">
      <span>Count（.NET 定时器推送）</span>
      <strong>{{ props.model.count }}</strong>
    </div>
    <div class="row">
      <span>实例标识（只读，区分共享/独立）</span>
      <code>{{ props.model.instanceId }}</code>
    </div>
    <div class="row">
      <button class="btn primary" @click="props.demo?.openMulti()">打开多窗口共享演示</button>
      <span class="muted">共享A/B 绑同一实例 + 独立实例（共 3 个窗口）</span>
    </div>
  </section>
</template>
