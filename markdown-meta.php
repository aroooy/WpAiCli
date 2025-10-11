<?php
/**
 * Plugin Name: Site Core - Markdown Meta
 * Description: Register markdown meta for REST and transform to HTML via Jetpack.
 * Author: You
 * Version: 1.0.0
 */

// 1) REST から扱えるメタを登録
add_action('init', function () {
    register_post_meta('post', '_md_source', [
        'type'              => 'string',
        'single'            => true,
        'show_in_rest'      => true, // これで REST から読み書き可能に
        'auth_callback'     => function($allowed, $meta_key, $post_id) {
            return current_user_can('edit_post', $post_id);
        },
        // Markdownをそのまま保持したいなら sanitize は外す/独自実装に
        'sanitize_callback' => null,
    ]);
});

// 2) REST 経由で _md_source が来たら HTML に変換して post_content に反映
add_action('rest_after_insert_post', function (WP_Post $post) {
    $md = get_post_meta($post->ID, '_md_source', true);
    if ($md === '' || $md === null) return;

    if (class_exists('WPCom_Markdown')) {
        $html = WPCom_Markdown::get_instance()->transform($md);
    } else {
        // Jetpack無効時のフォールバック（必要に応じて差し替え）
        $html = $md;
    }

    // ループ防止
    remove_action('rest_after_insert_post', __FUNCTION__, 10);
    wp_update_post([
        'ID'           => $post->ID,
        'post_content' => $html,
    ]);
    add_action('rest_after_insert_post', __FUNCTION__, 10, 1);
}, 10, 1);
